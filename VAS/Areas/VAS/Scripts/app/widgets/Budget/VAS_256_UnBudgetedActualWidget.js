/************************************************************
 * Module Name    : VAS
 * Purpose        : Unbudgeted Actuals - a 6x2 paginated exception list for the
 *                  Budgeting dashboard.
 *
 *                  The expense accounts that carry Actual postings in the selected
 *                  financial year with NO matching budget line behind them - money that
 *                  left the business outside the budget structure:
 *
 *                    [!] Unbudgeted actuals                        [ FY 2026 v ]
 *                        $412K posted to expense accounts with no budget line
 *
 *                    Account                    Organization Unit  Amount  Last posted
 *                    5210 — Freight outward     Mumbai Plant        $168K  28 Aug 2026
 *                    5480 — Legal and prof...   Head Office          $96K  26 Aug 2026
 *                    5115 — Site security       -                    $74K  25 Aug 2026
 *
 *                    Showing 1–3 of 5                                     <  1 of 2  >
 *
 *                  EXISTENCE, NOT COMPARISON. This is not a variance card. A row appears
 *                  when an Actual posting exists and a Budget posting for the same
 *                  account, the same transaction organization, the same financial year
 *                  and the same accounting schema does not. A budget of zero is still a
 *                  budget and suppresses the row.
 *
 *                  A NET-ZERO ACTUAL NEVER REACHES THIS FILE. The server drops any
 *                  account / organization whose debits and credits cancel out over the
 *                  year - nothing left the business, so there is no exposure to report
 *                  and a $0.00 line would spend one of three rows saying nothing. The
 *                  subtitle total is unaffected, since those groups contributed nothing
 *                  to it in the first place.
 *
 *                  THE ORGANIZATION UNIT IS AD_OrgTrx_ID, the TRANSACTION organization -
 *                  never Fact_Acct.AD_Org_ID. A posting with no transaction organization
 *                  is a dimension value of its own; it is NOT the same as organization 0,
 *                  which is the tenant-wide '*' organization and has a real name. The
 *                  server sends both the id and an IsOrgTrxNull flag, and the row hands
 *                  both straight back when it opens the dialog, so the drill-down
 *                  reconciles to the row that opened it.
 *
 *                  A MISSING VALUE IS A DASH, never a blank cell and never an invented
 *                  word. A blank cell reads as a rendering fault, and a label like
 *                  "Unassigned" reads as a value the ledger holds when in fact the ledger
 *                  holds nothing there. WHERE the dash sits in its cell is the
 *                  stylesheet's business, not this file's - see .vas-256-nil, which
 *                  aligns it per column - so nothing here should assume a direction.
 *
 *                  ONE CURRENCY, THE SCHEMA'S OWN. Every figure is an accounting amount
 *                  in the primary accounting schema's currency, printed with that
 *                  currency's symbol against the number - "$168K", never "USD 168K" -
 *                  falling back to the ISO code only when the currency has no symbol.
 *                  Nothing is converted and no scale is assumed: the ISO code decides
 *                  whether the compact form steps in lakh/crore or thousand/million.
 *
 *                  EVERY ROW IS A BUTTON. The row opens the postings behind it, so it is
 *                  reachable and activatable from the keyboard and carries an aria-label
 *                  naming the account it opens.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, Grid Data Rows, Widget Footer Pager, Content Fit Budget, No
 *                  Inner Scrollbars) supplies the shell, the row grid and the pager;
 *                  windows-and-panels.md > Panel Foundation supplies the drill-down
 *                  dialog. The widget specification supplies the four columns, the
 *                  subtitle sentence and the financial-year filter.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                       | Message Key
 *                  ---+------------------------------------+--------------------------
 *                   1 | Unbudgeted Actuals                 | VAS_256_UnBudgetedActual
 *                   2 | posted to expense accounts with   | VAS_256_NoMatchingBudget
 *                     |   no budget line                   |
 *                   3 | Organization Unit                  | VAS_256_OrganizationUnit
 *                   4 | Last posted                        | VAS_256_LastPosted
 *                   5 | Financial year                     | VAS_256_FinancialYear
 *                   6 | No unbudgeted actuals found for    | VAS_256_NoUnbudgeted
 *                     |   the selected financial year.     |
 *                   7 | Transactions                       | VAS_256_Transactions
 *                   8 | No transactions found.             | VAS_256_NoTransactions
 *                   9 | Net amount                         | VAS_256_NetAmount
 *                  10 | Description                        | VAS_256_Description
 *                  11 | Business partner                   | VAS_256_BusinessPartner
 *                  12 | No financial years available       | VAS_256_NoYears
 *                  13 | No primary calendar is configured  | VAS_256_NoCalendar
 *                  14 | No primary accounting schema is    | VAS_256_NoAcctSchema
 *                     |   configured                       |
 *                  15 | Account                            | VAS_234_Account   (reuse)
 *                  16 | Amount                             | Amount            (reuse)
 *                  17 | Date                               | Date              (reuse)
 *                  18 | Screen                             | VAS_202_Screen    (reuse)
 *                  19 | Debit                              | VAS_202_Debit     (reuse)
 *                  20 | Credit                             | VAS_202_Credit    (reuse)
 *                  21 | Close                              | VAS_018_Close     (reuse)
 *                  22 | Showing                            | VAS_020_Showing   (reuse)
 *                  23 | of                                 | VAS_020_Of        (reuse)
 *                  24 | Previous                           | VAS_020_Prev      (reuse)
 *                  25 | Next                               | VAS_020_Next      (reuse)
 *                  26 | Couldn't load                      | VAS_192_CouldntLoad (reuse)
 *
 *                  The missing-value dash is a GLYPH, not a message: it carries no
 *                  language and therefore needs no AD_Message key.
 *
 * Chronological development:
 *   VAI145         Created  Date 2026-09-08
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_256_UnBudgetedActualWidget.css. All classes
       are namespaced `vas-256-` so they never collide with sibling widgets. */

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
        ledger: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"></path>' +
            '<path d="M14 2v5h5"></path><path d="M12 11v3"></path><path d="M12 17.5h.01"></path></svg>',
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
            '<polyline points="9 18 15 12 9 6"></polyline></svg>',
        close: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M18 6 6 18"></path><path d="M6 6l12 12"></path></svg>'
    };

    /* Widget paging. The layout shows three rows; the adaptive fit may ask for more in a
       taller cell, and the server clamps whatever is asked for. */
    var DEFAULT_PAGE_SIZE = 3;
    var MIN_PAGE_SIZE = 1;
    var MAX_PAGE_SIZE = 12;
    var ROW_HEIGHT_FALLBACK = 44;

    /* The missing-value placeholder, shared by the card and the dialog. A GLYPH rather
       than a word: it needs no AD_Message key and reads the same in every language, where
       "Unassigned" or "N/A" would both need translating and would read as a value the
       ledger actually holds. Its alignment within the cell belongs to .vas-256-nil in the
       stylesheet, which sets it per column. */
    var NIL = '-';

    /* Drill-down paging - a document-baseline surface with far more room than a cell. */
    var MODAL_PAGE_SIZE = 8;

    /* First-paint ESTIMATES of the dialog's row and header heights, in px. They only set
       the body's opening height; syncModalHeight() then grows it to what a full page
       actually measures. They are deliberately a little UNDER the real thing rather than
       over: the body only ever grows, so an over-estimate would leave dead space below
       the last row for the life of the dialog. Sized against the panel anchor's own
       scale - a 0.75em row on a 16-18px anchor - not against the root font. */
    var MODAL_ROW_H = 38;
    var MODAL_HEAD_H = 32;
    var MODAL_BODY_SLACK = 12;

    VAS.VAS_256_UnBudgetedActualWidget = function () {
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
        var $overlay;
        var $modalBody;
        var $modalPager;
        var $modalBusy;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard,
           and both the picker and the dialog bind document-level handlers. */
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
        var _totalAmount = 0;

        var _rowH = 0;
        var _needsSync = false;
        var _loading = false;
        var _pickerOpen = false;
        var _disposed = false;
        var _rootObserver = null;
        var _listObserver = null;

        /* Drill-down state. _detailSeq drops a page that is overtaken by a newer one. */
        var _modalOpen = false;
        var _detailSeq = 0;
        var _modalPage = 1;
        var _modalRow = null;
        var _modalBodyH = 0;
        var _returnFocusTo = null;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas256_' + widgetID;

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
           re-reads the whole year, budgets may have been posted since the last load, and
           the page the user was on is not reliably the same page afterwards. The chosen
           YEAR is kept - that is a filter the user set, not a position in a list. */
        this.refreshWidget = function () {
            closePicker();
            closeModal();
            fetchPage(1);
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-256-root" id="vas-256-root-' + widgetID + '"></div>');

            var title = label('VAS_256_UnBudgetedActual', 'Unbudgeted Actuals');

            $card = $(
                '<div class="vas-256-card">' +
                    '<div class="vas-256-header">' +
                        '<span class="vas-256-icon">' + ICONS.ledger + '</span>' +
                        '<div class="vas-256-head-text">' +
                            '<div class="vas-256-title"></div>' +
                            '<div class="vas-256-subtitle"></div>' +
                        '</div>' +
                        '<button type="button" class="vas-256-year" aria-haspopup="listbox">' +
                            '<span class="vas-256-year-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-256-body" role="table">' +
                        '<div class="vas-256-ghead vas-256-row" role="row"></div>' +
                        '<div class="vas-256-list" role="rowgroup"></div>' +
                        '<div class="vas-256-pagerwrap"></div>' +
                    '</div>' +
                    '<div class="vas-256-state vas-256-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-256-title').text(title).attr('title', title);
            $card.find('.vas-256-body').attr('aria-label', title);

            $yearBtn = $card.find('.vas-256-year');
            $list = $card.find('.vas-256-list');
            $foot = $card.find('.vas-256-pagerwrap');
            $state = $card.find('.vas-256-state');

            paintHead();
            paintYearLabel();

            $yearBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated, so a repaint never has to rebind. The row IS the affordance -
               there is no separate "open" control to hunt for.

               WHO GETS FOCUS BACK when the dialog closes depends on how the row was
               activated, and e.detail is what tells us: a real pointer click reports the
               click count (1 or more), while Enter / Space on a focused button fires a
               click with detail 0. A keyboard user MUST land back on the row they opened -
               dropping them at the top of the document would strand them. A mouse user
               must NOT: they never asked for a focus ring, and restoring focus leaves the
               row outlined as though it were still selected long after the dialog is
               gone. So only a keyboard activation records a return target. */
            $list.on('click' + _ns, '.vas-256-brow', function (e) {
                var fromKeyboard = !e.detail;
                openModal(parseInt($(this).attr('data-index'), 10), fromKeyboard ? this : null);
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
                url: VIS.Application.contextUrl + 'VAS_256_UnBudgetedActualWidget/GetRows',
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
                    _totalAmount = Number(data.TotalAmount) || 0;

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
           unbudgeted does NOT - that is handled inside the list, so the header, the
           subtitle and the column labels stay put. */
        function renderState(text) {
            $card.find('.vas-256-body').addClass('vas-256-hidden');
            $state.removeClass('vas-256-hidden').text(text);
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

        /* "$412K posted to expense accounts with no budget line".

           The headline total lives in the subtitle rather than in a stat block, which
           keeps the card to the two stacking elements (rows, footer) that fit an Nx2 cell.
           It is the total across EVERY page, so it does not change when the reader turns
           one, and it carries no posting count: this card is about exposure, not volume.

           The wording names the SCOPE the title cannot. "Unbudgeted actuals" alone leaves
           two questions a reader has to guess at: which accounts (only AccountType 'E' -
           revenue, asset and liability accounts are out of scope and nothing on screen
           would otherwise say so), and what "unbudgeted" is measured against (a budget
           LINE existing at all, not a budget being exceeded - this is not a variance
           card). "posted to expense accounts with no budget line" answers both in the one
           line the header has room for. The financial year is NOT repeated here - the
           pill beside it already states which year every figure belongs to. */
        function paintSubtitle() {
            var text = compactAmount(_totalAmount) + ' ' +
                label('VAS_256_NoMatchingBudget', 'posted to expense accounts with no budget line');

            $card.find('.vas-256-subtitle').text(text).attr('title', text);
        }

        function paintYearLabel() {
            var text = _fiscalYear || yearNameOf(_yearId);
            $yearBtn.find('.vas-256-year-label').text(text);
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
            $card.find('.vas-256-ghead').html(
                '<span role="columnheader">' + escapeHtml(label('VAS_234_Account', 'Account')) + '</span>' +
                '<span role="columnheader">' +
                    escapeHtml(label('VAS_256_OrganizationUnit', 'Organization Unit')) + '</span>' +
                /* Amount is right-aligned - the one column a reader scans vertically. */
                '<span class="vas-256-num" role="columnheader">' +
                    escapeHtml(label('Amount', 'Amount')) + '</span>' +
                '<span class="vas-256-num" role="columnheader">' +
                    escapeHtml(label('VAS_256_LastPosted', 'Last posted')) + '</span>'
            );
        }

        function paintRows() {
            $state.addClass('vas-256-hidden');
            $card.find('.vas-256-body').removeClass('vas-256-hidden');

            if (!_rows || _rows.length === 0) {
                /* Nothing unbudgeted is GOOD news, not an error - and with no rows there is
                   nothing to page, so the footer goes too. */
                $list.html('<div class="vas-256-empty">' +
                    escapeHtml(label('VAS_256_NoUnbudgeted',
                        'No unbudgeted actuals found for the selected financial year.')) + '</div>');
                $foot.empty();
                return;
            }

            var html = '';
            for (var i = 0; i < _rows.length; i++) { html += rowHtml(_rows[i], i); }
            $list.html(html);

            paintFooter();

            /* Adapt the page size to the list height ONLY on the first paint / after a
               resize - never on manual navigation, which would flip pageSize under the
               user and re-clamp the page they just moved to. */
            if (_needsSync) { scheduleSync(); }
        }

        /* A real <button>, not a styled div: the row opens the postings behind it, so it
           has to be reachable and activatable from the keyboard, and its aria-label names
           the account it opens rather than leaving a screen reader to read four cells. */
        function rowHtml(item, index) {
            var account = accountText(item);
            var orgUnit = orgUnitText(item);

            return '<button type="button" class="vas-256-brow vas-256-row" role="row" ' +
                        'data-index="' + index + '" ' +
                        'aria-label="' + escapeHtml(rowAriaLabel(account)) + '" ' +
                        'title="' + escapeHtml(rowTooltip(item, account, orgUnit)) + '">' +
                bodyCell('vas-256-account', account) +
                bodyCell('vas-256-dim', orgUnit) +
                bodyCell('vas-256-amt vas-256-num', compactAmount(Number(item.Amount) || 0)) +
                bodyCell('vas-256-posted vas-256-num', formatDate(item.LastPosted)) +
            '</button>';
        }

        /* One body cell of a widget row. An empty value renders the missing-value dash
           instead of nothing at all - see NIL and nilCell below. The cell keeps its own
           class either way, which is how .vas-256-nil can align the dash per column. */
        function bodyCell(cls, text) {
            if (isBlank(text)) { return nilCell('span', cls, 'cell'); }

            return '<span class="' + cls + '" role="cell" title="' + escapeHtml(text) + '">' +
                escapeHtml(text) + '</span>';
        }

        /* "{Account Value} — {Account Name}". Either half may be missing on a badly seeded
           chart of accounts, so the em dash is only printed when there are two sides to
           it, and a row with neither falls through to the missing-value dash. */
        function accountText(item) {
            var value = item.AccountValue || '';
            var name = item.AccountName || '';

            if (value && name) { return value + ' — ' + name; }
            return value || name;
        }

        /* The Organization Unit cell: AD_Org.Name of the posting's AD_OrgTrx_ID. A posting
           with no transaction organization has no name to print - and inventing one
           ("Unassigned") would read as a value the ledger holds - so it falls through to
           the missing-value dash. It is NOT organization 0, which is the tenant-wide '*'
           organization and carries a real name. */
        function orgUnitText(item) {
            return item.Dimension || '';
        }

        function rowAriaLabel(account) {
            return label('VAS_256_Transactions', 'Transactions') + ': ' + account;
        }

        /* Everything the row holds, at full precision - the cells above are compact. A
           field the ledger does not carry is stated as the same dash the cell shows,
           rather than left out of the tooltip and leaving the reader to wonder. */
        function rowTooltip(item, account, orgUnit) {
            var lines = [];

            lines.push(isBlank(account) ? NIL : account);
            lines.push(label('VAS_256_OrganizationUnit', 'Organization Unit') + ': ' +
                (isBlank(orgUnit) ? NIL : orgUnit));
            lines.push(label('Amount', 'Amount') + ': ' + fullAmount(Number(item.Amount) || 0));

            lines.push(label('VAS_256_LastPosted', 'Last posted') + ': ' +
                (item.LastPosted ? formatDate(item.LastPosted) : NIL));

            return lines.join('\n');
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a–b of N" left, compact
             prev / next control right. Hidden on a single page. No posting count anywhere
             in it, and no "select a row" hint either - the rows are buttons and lift on
             hover, so the affordance says it better than a caption can, and the footer
             stays the one line of counting it is meant to be. ---- */
        function paintFooter() {
            if (_totalPages <= 1) { $foot.empty(); return; }

            var from = (_page - 1) * _pageSize + 1;
            var to = Math.min(_page * _pageSize, _totalRows);

            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _totalRows;

            var prevDis = _page <= 1 ? ' disabled' : '';
            var nextDis = _page >= _totalPages ? ' disabled' : '';

            $foot.html(
                '<div class="vas-256-pager">' +
                    '<span class="vas-256-pager-info" title="' + escapeHtml(showing) + '">' +
                        escapeHtml(showing) + '</span>' +
                    '<div class="vas-256-pager-nav">' +
                        '<button type="button" class="vas-256-pgbtn vas-256-pg-prev" aria-label="' +
                            escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                        '<span class="vas-256-pager-label">' + _page + ' ' +
                            escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _totalPages + '</span>' +
                        '<button type="button" class="vas-256-pgbtn vas-256-pg-next" aria-label="' +
                            escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                    '</div>' +
                '</div>'
            );

            /* A page change keeps the year filter - only the page number moves. */
            $foot.find('.vas-256-pg-prev').on('click', function () {
                if (!_loading && _page > 1) { fetchPage(_page - 1); }
            });
            $foot.find('.vas-256-pg-next').on('click', function () {
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
            var rendered = $list[0].querySelectorAll('.vas-256-brow');
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
            $picker = $('<div class="vas-256-pp vas-256-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-256-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectYear(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-256-pp-h">' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '</div>';

            if (_years.length === 0) {
                html += '<div class="vas-256-pp-empty">' +
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

            return '<button type="button" class="vas-256-pp-opt" role="option" data-id="' + id +
                    '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-256-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-256-pp-tick">' + ICONS.tick + '</span>' +
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
            $picker.removeClass('vas-256-hidden');
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
            if ($picker) { $picker.addClass('vas-256-hidden'); }

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
           on the same page of a different year, and the total has to be recomputed. Any
           open dialog goes with it: postings of the previous year must never stay on
           screen under a new year's heading. */
        function selectYear(id) {
            if (id <= 0 || id === _yearId) { return; }

            closeModal();

            _yearId = id;
            _fiscalYear = yearNameOf(id);
            paintYearLabel();
            fetchPage(1);
        }

        /* ------------------------------------------------------------ */
        /* Drill-down dialog - the Actual postings behind one row        */
        /* ------------------------------------------------------------ */
        function buildModal() {
            $overlay = $(
                '<div class="vas-256-overlay vas-256-hidden">' +
                    '<div class="vas-256-modal" role="dialog" aria-modal="true" aria-labelledby="' +
                        modalTitleId() + '">' +
                        '<div class="vas-256-modal-head">' +
                            '<span class="vas-256-modal-ico">' + ICONS.ledger + '</span>' +
                            '<div class="vas-256-modal-heads">' +
                                '<div class="vas-256-modal-title" id="' + modalTitleId() + '"></div>' +
                                '<div class="vas-256-modal-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-256-modal-close" aria-label="' +
                                escapeHtml(label('VAS_018_Close', 'Close')) + '">' + ICONS.close + '</button>' +
                        '</div>' +
                        '<div class="vas-256-modal-body"></div>' +
                        '<div class="vas-256-modal-foot"></div>' +
                        /* Sits over the panel, not over the body, so the header and the
                           pager stay readable while a page is in flight. */
                        '<div class="vas-256-modal-busy vas-256-hidden">' +
                            '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($overlay);
            $modalBody = $overlay.find('.vas-256-modal-body');
            $modalPager = $overlay.find('.vas-256-modal-foot');
            $modalBusy = $overlay.find('.vas-256-modal-busy');

            /* The body holds exactly one page, so its height is FIXED - not merely
               floored. syncModalHeight() grows it to the measured height of a real full
               page on the first paint. */
            _modalBodyH = (MODAL_ROW_H * MODAL_PAGE_SIZE) + MODAL_HEAD_H + MODAL_BODY_SLACK;
            $modalBody.css('height', _modalBodyH + 'px');

            /* The close button, Escape, and a click on the scrim - all three dismiss. */
            $overlay.find('.vas-256-modal-close').on('click', closeModal);
            $overlay.on('mousedown', function (e) {
                if (e.target === $overlay[0]) { closeModal(); }
            });

            $modalPager.on('click', '.vas-256-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _modalPage = $btn.attr('data-dir') === 'next' ? _modalPage + 1 : Math.max(1, _modalPage - 1);
                loadModalPage();
            });

            /* The source screen IS the zoom affordance - it opens the posting's own
               window, positioned on the record Fact_Acct points at. */
            $modalBody.on('click', '.vas-256-doclink', function (e) {
                e.stopPropagation();
                var $link = $(this);
                zoomTo($link.attr('data-key'),
                    parseInt($link.attr('data-record'), 10) || 0,
                    parseInt($link.attr('data-window'), 10) || 0);
            });
        }

        /* A per-instance id, so two of this widget on one dashboard do not both label
           their dialog by the same element. */
        function modalTitleId() {
            return 'vas256ModalTitle_' + widgetID;
        }

        function openModal(index, rowEl) {
            if (isNaN(index) || index < 0 || index >= _rows.length) { return; }
            if (!$overlay) { buildModal(); }

            _modalRow = _rows[index];
            _modalPage = 1;
            _returnFocusTo = rowEl || null;

            var titleText = accountText(_modalRow);
            $overlay.find('.vas-256-modal-title').text(isBlank(titleText) ? NIL : titleText);
            $overlay.find('.vas-256-modal-sub').text(modalSubtitle(_modalRow));

            /* Nothing of the previous row may show through: the body opens empty (at its
               pinned height) with the indicator over it. */
            $modalBody.empty();
            $modalPager.empty();

            $overlay.removeClass('vas-256-hidden');
            _modalOpen = true;
            closePicker();

            $(document).on('keydown' + _ns + 'm', onModalKeyDown);
            $overlay.find('.vas-256-modal-close').focus();

            loadModalPage();
        }

        /* "<Organization Unit> · <Fiscal year> · <Accounting schema> (<ISO>)" - the things
           that scope every figure in the dialog.

           A row with no organization unit drops the segment entirely rather than opening
           the line with a bare dash: this is running prose, not a grid cell, and a dash
           between two separators reads as a rendering fault. The dialog's own grid still
           shows the dash wherever a cell is empty. */
        function modalSubtitle(item) {
            var parts = [];

            var orgUnit = orgUnitText(item);
            if (!isBlank(orgUnit)) { parts.push(orgUnit); }

            if (_fiscalYear) { parts.push(_fiscalYear); }

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
            if ($overlay) { $overlay.addClass('vas-256-hidden'); }
            $(document).off('keydown' + _ns + 'm');

            /* Focus goes back to the row that opened the dialog - a keyboard user must not
               be dropped at the top of the document. */
            if (_returnFocusTo) {
                try { _returnFocusTo.focus(); } catch (e) { /* ignore */ }
                _returnFocusTo = null;
            }
        }

        function onModalKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
        }

        function showModalBusy(show) {
            if (!$modalBusy || !$modalBusy[0]) { return; }
            $modalBusy.toggleClass('vas-256-hidden', !show);
        }

        /* One page at a time from the server - the dialog never receives the whole set.
           The previous page stays painted underneath the busy indicator, so the panel
           neither blanks nor resizes while the next one arrives.

           The row's dimension travels back EXACTLY as it came: the id and the "no
           transaction organization" flag together, because organization 0 is the
           tenant-wide '*' org and cannot stand in for "none". */
        function loadModalPage() {
            if (!_modalRow) { return; }

            var mySeq = ++_detailSeq;
            var row = _modalRow;
            var yearId = _yearId;

            showModalBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_256_UnBudgetedActualWidget/GetTransactions',
                type: 'GET',
                dataType: 'json',
                cache: false,
                async: true,
                data: {
                    yearId: yearId,
                    accountId: Number(row.Account_ID) || 0,
                    orgTrxId: Number(row.AD_OrgTrx_ID) || 0,
                    isOrgTrxNull: row.IsOrgTrxNull ? 'Y' : 'N',
                    pageNo: _modalPage,
                    pageSize: MODAL_PAGE_SIZE
                },
                success: function (raw) {
                    /* Stale response: another page, another row, another year, or the
                       dialog has been closed since. */
                    if (mySeq !== _detailSeq || row !== _modalRow || yearId !== _yearId) { return; }

                    var data = parseResponse(raw);

                    if (!data || data.error) {
                        renderModalState(label('VAS_192_CouldntLoad', "Couldn't load"), true);
                        return;
                    }

                    if (data.ErrorCode) {
                        renderModalState(errorLabel(data.ErrorCode), true);
                        return;
                    }

                    if (data.Schema) { _schema = data.Schema; }

                    _modalPage = Number(data.Page) || 1;
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

        /* Empty / error takeover of the body. The footer keeps its pager row (with the
           controls disabled) rather than collapsing, so the dialog holds the same height
           in every state. */
        function renderModalState(text, isError) {
            $modalBody.html('<div class="vas-256-modal-state' + (isError ? ' vas-256-modal-state-error' : '') +
                '">' + escapeHtml(text) + '</div>');
            renderModalPager({ Total: 0, PageSize: MODAL_PAGE_SIZE, Page: 1, Rows: [] });
        }

        /* The seven columns of the posting list, in reading order: when it was posted,
           where it came from, what it said, WHO it was against, and the two sides plus
           their net effect.

           Business partner sits after Description because the two answer the same
           question from opposite ends - the description is what the posting says about
           itself, the partner is who it was actually with - and reading them together is
           what identifies a posting the reader half-remembers. It is deliberately not
           earlier: a great many unbudgeted expense postings (depreciation, accruals,
           manual journals) have no partner at all, and a column of dashes in third place
           would push the columns that always carry a value out to the right. */
        function modalColumns() {
            return [
                [label('Date', 'Date'), false],
                [label('VAS_202_Screen', 'Screen'), false],
                [label('VAS_256_Description', 'Description'), false],
                [label('VAS_256_BusinessPartner', 'Business partner'), false],
                [label('VAS_202_Debit', 'Debit'), true],
                [label('VAS_202_Credit', 'Credit'), true],
                [label('VAS_256_NetAmount', 'Net amount'), true]
            ];
        }

        function renderModalRows(data) {
            var rows = data.Rows || [];

            if (rows.length === 0) {
                renderModalState(label('VAS_256_NoTransactions', 'No transactions found.'), false);
                return;
            }

            var captions = modalColumns();

            var head = '<div class="vas-256-dhead">';
            for (var c = 0; c < captions.length; c++) {
                head += modalCell(captions[c][0], captions[c][1] ? 'vas-256-dcell-num' : '', true);
            }
            head += '</div>';

            var body = '';
            for (var i = 0; i < rows.length; i++) { body += modalRowHtml(rows[i]); }

            $modalBody.html('<div class="vas-256-dgrid">' + head + body + '</div>');
            $modalBody.scrollTop(0);

            syncModalHeight();
            renderModalPager(data);
        }

        /* One cell of the drill-down grid. An empty value renders the missing-value dash -
           the dialog and the card answer a missing value the same way. `isHead` keeps a column
           caption printing as a caption even in the (impossible) case of a blank label. */
        function modalCell(text, extraClass, isHead) {
            var cls = 'vas-256-dcell' + (extraClass ? ' ' + extraClass : '');

            if (!isHead && isBlank(text)) { return nilCell('span', cls, ''); }

            return '<span class="' + cls + '" title="' + escapeHtml(text) + '">' +
                escapeHtml(text) + '</span>';
        }

        /* The source screen rendered as a link. A button, not an anchor: there is no URL
           to follow - the framework opens the window. The SERVER decides whether the row
           is navigable at all (an active window the posting names, a real record, and a
           single-column key to position by); without that it stays plain text rather than
           offering a dead link. */
        function screenCell(row) {
            var text = row.ScreenDisplayName || '';

            /* Neither a name nor a window: nothing to print and nowhere to go. */
            if (isBlank(text)) { return nilCell('span', 'vas-256-dcell vas-256-dcell-b', ''); }

            if (!row.CanNavigate) {
                return '<span class="vas-256-dcell vas-256-dcell-b" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>';
            }

            return '<span class="vas-256-dcell vas-256-dcell-doc">' +
                '<button type="button" class="vas-256-doclink"' +
                    ' data-key="' + escapeHtml(row.KeyColumnName || '') + '"' +
                    ' data-record="' + (Number(row.Record_ID) || 0) + '"' +
                    ' data-window="' + (Number(row.AD_Window_ID) || 0) + '"' +
                    ' title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</button>' +
            '</span>';
        }

        /* A zero side prints as the same dash a missing value gets: a posting sits
           on ONE side of the account, so the other side is not a nought the reader should
           weigh - it is simply not there. Printing "0.00" in both columns would make them
           work out which side the posting was on. */
        function sideCell(value) {
            var v = Number(value) || 0;
            if (v === 0) { return modalCell('', 'vas-256-dcell-num'); }
            return modalCell(fullAmount(v), 'vas-256-dcell-num');
        }

        /* The NET amount is never dashed, even at zero: it is the row's conclusion, and a
           genuinely zero net effect is a finding rather than an absence. */
        function modalRowHtml(row) {
            return '<div class="vas-256-drow">' +
                modalCell(formatDate(row.DateAcct)) +
                screenCell(row) +
                modalCell(row.Description || '') +
                modalCell(row.BusinessPartner || '') +
                sideCell(row.Debit) +
                sideCell(row.Credit) +
                modalCell(fullAmount(Number(row.Amount) || 0), 'vas-256-dcell-num vas-256-dcell-amt') +
            '</div>';
        }

        /* Grows the fixed body to whatever a FULL page actually measures, so the last page
           never introduces a scrollbar the first page did not have. Only ever grows:
           shrinking back on a short page is the fluctuation this prevents. */
        function syncModalHeight() {
            if (!$modalBody || !$modalBody[0]) { return; }

            var headEl = $modalBody[0].querySelector('.vas-256-dhead');
            var rowEls = $modalBody[0].querySelectorAll('.vas-256-drow');
            if (!rowEls.length) { return; }

            var rowH = 0;
            for (var i = 0; i < rowEls.length; i++) {
                if (rowEls[i].offsetHeight > rowH) { rowH = rowEls[i].offsetHeight; }
            }
            if (rowH <= 0) { return; }

            var headH = headEl ? headEl.offsetHeight : MODAL_HEAD_H;
            var needed = headH + (rowH * MODAL_PAGE_SIZE) + MODAL_BODY_SLACK;

            if (needed > _modalBodyH) {
                _modalBodyH = needed;
                $modalBody.css('height', _modalBodyH + 'px');
            }
        }

        /* The dialog's own footer pager, same shape as the card's. */
        function renderModalPager(data) {
            var total = Number(data.Total) || 0;
            var pageSize = Number(data.PageSize) || MODAL_PAGE_SIZE;
            var pageNo = Number(data.Page) || 1;
            var rows = (data.Rows || []).length;

            var totalPages = Math.max(1, Math.ceil(total / pageSize));
            var from = rows > 0 ? ((pageNo - 1) * pageSize) + 1 : 0;
            var to = rows > 0 ? from + rows - 1 : 0;

            var ofTxt = label('VAS_020_Of', 'of');
            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                ofTxt + ' ' + total;

            var prevDis = pageNo <= 1 ? ' disabled' : '';
            var nextDis = (rows === 0 || pageNo >= totalPages) ? ' disabled' : '';

            $modalPager.html(
                '<span class="vas-256-pager-info" title="' + escapeHtml(showing) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<span class="vas-256-pager-nav">' +
                    '<button type="button" class="vas-256-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                    '<span class="vas-256-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-256-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                '</span>'
            );
        }

        /* Opens the posting's source record in its own standard window. The window id
           comes from Fact_Acct.AD_Window_ID (already confirmed active server-side) and the
           key column from the source table's own dictionary metadata - so this widget can
           sit on any dashboard, far from the screen the posting came from, and still land
           on the right record. No window NAME is involved and no AD_Window_ID is ever
           hard-coded.

           The dialog closes first: the record opens in its own window, and leaving the
           dialog over it would hide what was just opened. Degrades silently - a click can
           never throw. */
        function zoomTo(keyColumnName, recordId, windowId) {
            if (!keyColumnName || recordId <= 0 || windowId <= 0) { return; }
            if (!VAS.ZoomUtil || typeof VAS.ZoomUtil.zoomToRecord !== 'function') { return; }

            closeModal();

            try {
                VAS.ZoomUtil.zoomToRecord(keyColumnName, recordId, windowId, '', '');
            }
            catch (e) {
                if (window.console) { console.log(e); }
            }
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

        /* The compact cell figure: the currency SYMBOL printed directly against the number
           - "$168K", "₹74K" - never "USD 168K", and only falling back to the ISO code when
           the currency has no symbol configured (which is what the server's own
           COALESCE(CurSymbol, ISO_Code) already resolved). A true minus sign, not a
           hyphen. The ISO code drives the scale, so no lakh/crore or million step is ever
           assumed. */
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

        /* Full, non-compact amount - for the row tooltip and the dialog's own columns,
           where the exact figure is the point. Grouping and the decimal separator come
           from the browser locale, the decimals from the schema currency's precision. */
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

        /* Dates arrive as yyyy-MM-dd and are rendered in the browser's locale, WITH the
           year - a financial year can straddle two calendar years, so "28 Aug" alone
           leaves the reader guessing. Parsed part by part rather than through
           Date(string), which reads a bare ISO date as UTC and shifts it a day back for
           anyone west of Greenwich. */
        function formatDate(isoDate) {
            /* Empty, NOT a dash. Every cell helper turns an empty value into the
               missing-value dash and tags the cell so the stylesheet can align it;
               returning a dash here would slip past that and be laid out as though it
               were a real date. */
            if (!isoDate) { return ''; }

            var parts = String(isoDate).split('-');
            if (parts.length !== 3) { return String(isoDate); }

            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return String(isoDate); }

            try {
                return d.toLocaleDateString(window.navigator.language,
                    { day: '2-digit', month: 'short', year: 'numeric' });
            }
            catch (e) { return String(isoDate); }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* True when a value is nothing the reader can be shown - null, undefined or an
           empty string. A zero is NOT blank: it is a figure, and this card has columns
           where nought is a real answer. */
        function isBlank(value) {
            return value === null || value === undefined || String(value) === '';
        }

        /* One cell holding the missing-value placeholder. It keeps the cell's own class and
           adds .vas-256-nil, which is what lets the stylesheet align the dash per column -
           and is why the dash is produced HERE rather than by each formatter: a dash
           returned as a cell's text would carry no marker and be laid out as a real value.
           No title attribute - there is nothing fuller to reveal. */
        function nilCell(tag, cls, role) {
            return '<' + tag + ' class="' + cls + ' vas-256-nil"' +
                (role ? ' role="' + role + '"' : '') + '>' + NIL + '</' + tag + '>';
        }

        /* Every database-sourced string - and a posting description is user-supplied text
           - goes through here before it reaches the DOM. */
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

        /* Release everything that outlives the card: the body-mounted picker and dialog,
           the document and window listeners they register, and both observers - a
           ResizeObserver left running keeps the whole subtree alive. */
        this.releasePanel = function () {
            _disposed = true;

            closePicker();
            closeModal();

            if (_rootObserver) {
                try { _rootObserver.disconnect(); } catch (e) { /* ignore */ }
                _rootObserver = null;
            }
            if (_listObserver) {
                try { _listObserver.disconnect(); } catch (e) { /* ignore */ }
                _listObserver = null;
            }

            if ($picker) { $picker.off(); $picker.remove(); $picker = null; }

            if ($overlay) {
                $overlay.off();
                $overlay.find('*').off();
                $overlay.remove();
                $overlay = null;
                $modalBody = null;
                $modalPager = null;
                $modalBusy = null;
            }

            if ($busy) { $busy.remove(); $busy = null; }
            if ($yearBtn) { $yearBtn.off(_ns); }
            if ($list) { $list.off(_ns); }
            if ($foot) { $foot.off(); }

            _rows = [];
            _years = [];
            _modalRow = null;
            _returnFocusTo = null;
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_256_UnBudgetedActualWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_256_UnBudgetedActualWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_256_UnBudgetedActualWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
