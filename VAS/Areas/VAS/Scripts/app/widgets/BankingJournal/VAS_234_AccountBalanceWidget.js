/************************************************************
 * Module Name    : VAS
 * Purpose        : Account-wise Balance - a row table for the Banking dashboard.
 *
 *                  One row per active bank account: what moved OUT, what moved IN,
 *                  the net of the two on a centred diverging bar, and what the
 *                  account closed the period at.
 *
 *                    [bank] Account-wise balance            [ Net variance v ]
 *                           Inflow, outflow and net variance · Sep-2026
 *
 *                    Account            ← Outflow   Net   Inflow →   Closing balance
 *                    Uco Bank Checking     96.20K +32.20K  128.40K         ₹471.90K
 *                    9032001122 · INR      ███████|██████████
 *                    Axis Payroll            No activity this period            ₹0.00
 *                    3390004455 · INR              |
 *
 *                    Showing 1–5 of 45 accounts              <  Page 1 of 9  >
 *
 *                  THE CENTRE-AXIS FLOW BAR IS THE POINT OF THE WIDGET. Outflow
 *                  grows LEFT of the axis, inflow grows RIGHT, and both are drawn
 *                  against ONE scale shared by every row on every page - the largest
 *                  single gross flow in the whole accessible set, which the server
 *                  returns as MaxFlow. The asymmetry between the two sides IS the
 *                  net. It is not a progress bar, not a balance-magnitude bar and
 *                  not a stacked bar, and the bar widths are never derived from the
 *                  closing balance.
 *
 *                  NOTHING IS COMPARED WITH A PREVIOUS PERIOD. The card reports what
 *                  this period did (outflow, inflow, net) and where the account
 *                  stands now (closing balance). No prior-period balance is read,
 *                  named or shown, so the subtitle names one period only and the
 *                  closing balance carries no delta beneath it.
 *
 *                  Every amount in a row is in THAT ACCOUNT's own currency, with its
 *                  own symbol and StdPrecision, formatted through the shared
 *                  VIS.Util.formatCompactAmount helper
 *                  (Scripts/app/util/CurrencyFormat.js) - no tenant currency is ever
 *                  assumed. The three flow figures are bare magnitudes; only the
 *                  closing balance carries the symbol, because it is the row's one
 *                  absolute figure and the account's ISO code is already under its
 *                  name.
 *
 *                  Sort, paging and the period are all server-resolved. The control
 *                  sends a KEY from a fixed list, never an expression; a page or sort
 *                  change is one request and preserves the other two.
 *
 *                  Design: design.md -> dashboard-widgets.md supplies the shell
 *                  (Glass Widget, Widget Header, Grid Data Rows, Widget Footer Pager,
 *                  No Inner Scrollbars) and the header typography; the widget
 *                  specification supplies the row anatomy and the flow semantics,
 *                  including its named inflow / outflow tones.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                  | Message Key
 *                  ---+-------------------------------+-----------------------------
 *                   1 | Account-wise balance          | VAS_234_AccountBalance
 *                   2 | Inflow, outflow and net       | VAS_234_AccountBalanceHint
 *                     |   variance                    |
 *                   3 | Account                       | VAS_234_Account
 *                   4 | Outflow                       | VAS_234_Outflow
 *                   5 | Inflow                        | VAS_234_Inflow
 *                   6 | Net                           | VAS_234_Net
 *                   7 | Closing balance               | VAS_234_ClosingBalance
 *                   8 | Net variance                  | VAS_234_NetVariance
 *                   9 | Account name                  | VAS_234_AccountName
 *                  10 | Sort by                       | VAS_234_SortBy
 *                  11 | No activity this period       | VAS_234_NoActivity
 *                  12 | No bank accounts available    | VAS_234_NoAccounts
 *                  13 | accounts                      | VAS_234_Accounts
 *                  14 | Page                          | VAS_234_Page
 *                  15 | Showing                       | VAS_020_Showing   (reuse)
 *                  16 | of                            | VAS_020_Of        (reuse)
 *                  17 | Previous                      | VAS_020_Prev      (reuse)
 *                  18 | Next                          | VAS_020_Next      (reuse)
 *                  19 | Couldn't load                 | VAS_192_CouldntLoad (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-03
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_234_AccountBalanceWidget.css. All classes
       are namespaced `vas-234-` so they never collide with sibling widgets. */

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
       font and a missing glyph leaves an empty box. Every one is decorative and carries
       aria-hidden; the meaning is always in the text beside it. */
    var ICONS = {
        bank: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M3 10h18M5 10v9M9 10v9M15 10v9M19 10v9M3 19h18M12 3 3 8h18Z"></path></svg>',
        sort: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M3 8h13M3 16h9M18 5v14M18 19l3-3M18 19l-3-3"></path></svg>',
        chevron: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>',
        prev: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="m15 6-6 6 6 6"></path></svg>',
        next: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="m9 6 6 6-6 6"></path></svg>'
    };

    /* The sort whitelist, mirroring the server's SORT_* constants. The control sends one
       of these KEYS and nothing else - no expression, no column name, no SQL. */
    var SORTS = [
        { key: 'netVariance', msg: 'VAS_234_NetVariance', text: 'Net variance' },
        { key: 'closingBalance', msg: 'VAS_234_ClosingBalance', text: 'Closing balance' },
        { key: 'inflow', msg: 'VAS_234_Inflow', text: 'Inflow' },
        { key: 'outflow', msg: 'VAS_234_Outflow', text: 'Outflow' },
        { key: 'accountName', msg: 'VAS_234_AccountName', text: 'Account name' }
    ];

    /* Zoom target. Resolved by NAME - AD_Window_ID differs per environment and must never
       be hard-coded (VAS.ZoomUtil tries the new name, then the old one, then
       VAS_ZoomScreenConfig). Drill-down is optional per the specification: rows only
       become clickable if the window actually resolves. */
    var ZOOM_COLUMN = 'C_BankAccount_ID';
    var ZOOM_WINDOW_NAME_NEW = 'VAS_BankAccount';
    var ZOOM_WINDOW_NAME_OLD = 'Bank Account';

    /* The specification fixes the page at five rows. The widget may ask for fewer when its
       cell genuinely cannot hold five - the design forbids an inner scrollbar, so a short
       cell pages more rather than clipping - but never for more. */
    var PAGE_SIZE = 5;
    var MIN_PAGE_SIZE = 1;
    var ROW_HEIGHT_FALLBACK = 58;

    VAS.VAS_234_AccountBalanceWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $subtitle;
        var $sortBtn;
        var $head;
        var $list;
        var $foot;
        var $state;
        var $picker;
        var $busy;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard,
           and the sort picker binds document-level handlers. */
        var _ns = '';

        var _rows = [];
        var _maxFlow = 0;
        var _sort = 'netVariance';
        var _page = 1;
        var _pageSize = PAGE_SIZE;
        var _totalRows = 0;
        var _totalPages = 0;
        var _currentLabel = '';

        var _rowH = 0;
        var _needsSync = false;
        var _loading = false;
        var _pickerOpen = false;
        var _zoomWindowId = 0;
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
            _ns = '.vas234_' + widgetID;

            buildSkeleton();
            createBusyIndicator();
            setupRootObserver();
            resolveZoomWindow();
        };

        /* The framework's own widget loader, overlaid on the whole card while the FIRST
           page is fetched - the same treatment every sibling VAS widget gives its initial
           load, so the dashboard shows one spinner style throughout. It is created visible
           so it covers the card from the moment the widget mounts, before any request has
           even been sent, and is hidden for good once the first response paints.

           Later loads (a page turn, a sort change, a refresh) do NOT raise it: those
           replace the list contents only, so the card keeps its header and footer and the
           localised list loader stands in instead. */
        function createBusyIndicator() {
            $busy = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
            '</div>');
            $busy[0].style.visibility = 'visible';
            $root.append($busy);
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
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
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
           re-reads the whole set, and accounts may have been added, removed or reordered
           since the last load - the page the user was on is not reliably the same page
           afterwards, and the total may no longer reach it. The chosen SORT is kept, since
           that is a preference the user set rather than a position in a list. */
        this.refreshWidget = function () {
            fetchPage(1);
        };

        /* Resolve the zoom window id ONCE, up front. Account names only render as links
           when it resolves - a link that navigates nowhere is worse than plain text.
           This races the first data load, so whichever finishes second paints the links:
           if rows are already on screen when the id arrives, they are repainted here. */
        function resolveZoomWindow() {
            if (!VAS.ZoomUtil || typeof VAS.ZoomUtil.getWindowId !== 'function') { return; }

            VAS.ZoomUtil.getWindowId(ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                .then(function (id) {
                    if (_disposed) { return; }

                    _zoomWindowId = Number(id) || 0;
                    if (_zoomWindowId > 0 && !_loading && _rows && _rows.length > 0) {
                        paintRows();
                    }
                });
        }

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-234-root" id="vas-234-root-' + widgetID + '"></div>');

            var title = label('VAS_234_AccountBalance', 'Account-wise balance');

            $card = $(
                '<div class="vas-234-card">' +
                    '<div class="vas-234-header">' +
                        '<span class="vas-234-icon">' + ICONS.bank + '</span>' +
                        '<div class="vas-234-head-text">' +
                            '<div class="vas-234-title"></div>' +
                            '<div class="vas-234-subtitle"></div>' +
                        '</div>' +
                        '<button type="button" class="vas-234-sort" aria-haspopup="listbox">' +
                            ICONS.sort +
                            '<span class="vas-234-sort-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    /* role="table" plus explicit row / columnheader / cell roles: the
                       layout is CSS grid, so the semantics have to be declared. */
                    '<div class="vas-234-body" role="table">' +
                        '<div class="vas-234-ghead vas-234-row" role="row"></div>' +
                        '<div class="vas-234-list" role="rowgroup"></div>' +
                    '</div>' +
                    '<div class="vas-234-foot"></div>' +
                    '<div class="vas-234-state vas-234-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-234-title').text(title).attr('title', title);
            $card.find('.vas-234-body').attr('aria-label', title);

            $subtitle = $card.find('.vas-234-subtitle');
            $sortBtn = $card.find('.vas-234-sort');
            $head = $card.find('.vas-234-ghead');
            $list = $card.find('.vas-234-list');
            $foot = $card.find('.vas-234-foot');
            $state = $card.find('.vas-234-state');

            paintHead();
            paintSortLabel();

            $sortBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated, so a repaint never has to rebind. Only the account-name link
               navigates - not the whole row: a row carries five figures a user may want
               to select or read, and a click anywhere on it launching a window would be a
               surprise rather than an affordance. */
            $list.on('click' + _ns, '.vas-234-namelink', function (e) {
                e.preventDefault();
                e.stopPropagation();
                zoomToAccount(parseInt($(this).attr('data-id'), 10) || 0);
            });

            /* An <a> without an href gets no implicit key activation, so Enter and Space
               are wired by hand - the link has to work from the keyboard as well as the
               mouse. */
            $list.on('keydown' + _ns, '.vas-234-namelink', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar' ||
                    e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault();
                    e.stopPropagation();
                    zoomToAccount(parseInt($(this).attr('data-id'), 10) || 0);
                }
            });

            $root.append($card);
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function fetchPage(pageNo) {
            if (_loading) { return; }
            _loading = true;
            renderLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_234_AccountBalanceWidget/GetAccountBalances',
                type: 'GET',
                dataType: 'json',
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: { pageNo: pageNo, pageSize: _pageSize, sortKey: _sort },
                success: function (raw) {
                    _loading = false;
                    if (_disposed) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error || !data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    _rows = data.Rows || [];
                    _maxFlow = Number(data.MaxFlow) || 0;
                    _sort = data.Sort || _sort;
                    _page = Number(data.Page) || 1;
                    _pageSize = Number(data.PageSize) || _pageSize;
                    _totalRows = Number(data.TotalRows) || 0;
                    _totalPages = Number(data.TotalPages) || 0;
                    _currentLabel = data.CurrentPeriodLabel || '';

                    paintSubtitle();
                    paintSortLabel();
                    paintRows();
                    observeList();
                },
                error: function () {
                    _loading = false;
                    if (_disposed) { return; }
                    /* The overlay comes down on failure too - leaving a spinner spinning
                       over an error the user cannot see is the worst of both. */
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

        /* A skeleton of the SAME shape as the page it is replacing - never the previous
           page's figures dimmed, which would read as live data for as long as the request
           takes. */
        function renderLoading() {
            $state.addClass('vas-234-hidden');
            $card.find('.vas-234-body').removeClass('vas-234-hidden');

            var rows = '';
            var count = _pageSize > 0 ? _pageSize : PAGE_SIZE;
            for (var i = 0; i < count; i++) {
                rows += '<div class="vas-234-skel">' +
                    '<span class="vas-234-skel-a"></span>' +
                    '<span class="vas-234-skel-b"></span>' +
                    '<span class="vas-234-skel-c"></span>' +
                '</div>';
            }
            $list.html(rows);
        }

        /* A load failure takes the card over; an empty account list does NOT - it renders
           inside the list, so the header and column labels stay put. */
        function renderState(text) {
            $card.find('.vas-234-body').addClass('vas-234-hidden');
            $foot.empty();
            $state.removeClass('vas-234-hidden').text(text);
        }

        /* "Inflow, outflow and net variance · Sep-2026" - the period label comes from the
           server, so nothing here is ever hard-coded. ONE period only: the card compares
           nothing against a previous one. */
        function paintSubtitle() {
            var parts = [label('VAS_234_AccountBalanceHint', 'Inflow, outflow and net variance')];
            if (_currentLabel) { parts.push(_currentLabel); }

            var text = parts.join(' · ');
            $subtitle.text(text).attr('title', text);
        }

        function paintSortLabel() {
            var text = sortText(_sort);
            $sortBtn.find('.vas-234-sort-label').text(text);
            $sortBtn.attr('title', label('VAS_234_SortBy', 'Sort by') + ': ' + text);
        }

        function sortText(key) {
            for (var i = 0; i < SORTS.length; i++) {
                if (SORTS[i].key === key) { return label(SORTS[i].msg, SORTS[i].text); }
            }
            return label(SORTS[0].msg, SORTS[0].text);
        }

        /* §Grid Data Rows header: transparent background, Medium, muted, with a divider
           under it. The two arrows say which way each side of the bar below grows. */
        function paintHead() {
            $head.html(
                '<span class="vas-234-acct" role="columnheader">' +
                    escapeHtml(label('VAS_234_Account', 'Account')) + '</span>' +
                '<span class="vas-234-flow" role="columnheader">' +
                    '<span class="vas-234-hf-o"><span class="vas-234-arrow" aria-hidden="true">&#8592;</span>' +
                        escapeHtml(label('VAS_234_Outflow', 'Outflow')) + '</span>' +
                    '<span class="vas-234-hf-n">' + escapeHtml(label('VAS_234_Net', 'Net')) + '</span>' +
                    '<span class="vas-234-hf-i">' + escapeHtml(label('VAS_234_Inflow', 'Inflow')) +
                        '<span class="vas-234-arrow" aria-hidden="true">&#8594;</span></span>' +
                '</span>' +
                '<span class="vas-234-bal" role="columnheader">' +
                    escapeHtml(label('VAS_234_ClosingBalance', 'Closing balance')) + '</span>'
            );
        }

        function paintRows() {
            $state.addClass('vas-234-hidden');
            $card.find('.vas-234-body').removeClass('vas-234-hidden');

            if (!_rows || _rows.length === 0) {
                $list.html('<div class="vas-234-empty">' +
                    escapeHtml(label('VAS_234_NoAccounts', 'No bank accounts available')) + '</div>');
                /* No rows means no pager - an empty pager is worse than none. */
                $foot.empty();
                return;
            }

            var html = '';
            for (var i = 0; i < _rows.length; i++) { html += rowHtml(_rows[i]); }
            $list.html(html);

            paintFooter();

            /* Adapt the page size to the list height ONLY on the first paint / after a
               resize - never on manual navigation, which would flip pageSize under the
               user and re-clamp the page they just moved to. */
            if (_needsSync) { scheduleSync(); }
        }

        /* Three cells - Account | Flow | Closing balance - exactly as the specification's
           row grid. The flow cell stacks its three figures directly over the bar segment
           each one describes. */
        function rowHtml(item) {
            /* Only the CLOSING BALANCE steps back on a quiet row. The account name stays
               a full-strength link either way - the Bank Account record it opens exists
               whether or not anything moved this period. */
            var quiet = item.HasActivity ? '' : ' vas-234-dim';

            return '<div class="vas-234-brow vas-234-row" role="row" data-id="' +
                        (Number(item.BankAccountId) || 0) + '" ' +
                        'title="' + escapeHtml(rowTooltip(item)) + '">' +

                '<div class="vas-234-acct" role="cell">' +
                    '<div class="vas-234-name">' + nameHtml(item) + '</div>' +
                    '<div class="vas-234-meta">' + escapeHtml(metaText(item)) + '</div>' +
                '</div>' +

                '<div class="vas-234-flow" role="cell">' +
                    numsHtml(item) +
                    barHtml(item) +
                '</div>' +

                '<div class="vas-234-bal" role="cell">' +
                    '<div class="vas-234-bal-v' + quiet + '">' + escapeHtml(money(item, item.ClosingBalance)) + '</div>' +
                '</div>' +
            '</div>';
        }

        /* The account name is the row's zoom link: it opens the Bank Account window on
           this record. A row whose id cannot be resolved - or a session where the window
           itself does not resolve - degrades to plain text, so there is never a dead link
           that looks live. role="link" + tabindex make it reachable from the keyboard,
           since an <a> carrying no href is neither by default. */
        function nameHtml(item) {
            var name = escapeHtml(item.AccountName || '');
            var accountId = Number(item.BankAccountId) || 0;

            if (accountId <= 0 || _zoomWindowId <= 0) { return name; }

            return '<a class="vas-234-namelink" role="link" tabindex="0" data-id="' + accountId + '">' +
                name +
            '</a>';
        }

        /* "9032001122 · INR" - the account number in full, and the account's own ISO code
           saying which currency every figure on this row is in. */
        function metaText(item) {
            var parts = [];
            if (item.AccountNo) { parts.push(item.AccountNo); }
            if (item.CurrencyCode) { parts.push(item.CurrencyCode); }
            return parts.join(' · ');
        }

        /* The three flow figures, each sitting over the bar segment it describes. A row
           with no movement at all says so in words instead - "no activity" is about the
           FLOWS, and the closing balance beside it still prints. */
        function numsHtml(item) {
            if (!item.HasActivity) {
                return '<div class="vas-234-nums vas-234-nums--quiet">' +
                    '<span class="vas-234-quiet">' +
                        escapeHtml(label('VAS_234_NoActivity', 'No activity this period')) +
                    '</span>' +
                '</div>';
            }

            var net = Number(item.Net) || 0;
            var netCls = net > 0 ? ' vas-234-up' : (net < 0 ? ' vas-234-down' : '');

            return '<div class="vas-234-nums">' +
                '<span class="vas-234-o">' + escapeHtml(flow(item, item.Outflow)) + '</span>' +
                '<span class="vas-234-n' + netCls + '">' + escapeHtml(signedFlow(item, net)) + '</span>' +
                '<span class="vas-234-i">' + escapeHtml(flow(item, item.Inflow)) + '</span>' +
            '</div>';
        }

        /* The bar itself. Widths come from the SERVER (each side already expressed as its
           share of the whole track, capped at 50%), so the client never re-derives the
           shared scale and the two can never disagree. The axis is always rendered, even
           on a row with no movement - it is the zero the two sides are measured from. */
        function barHtml(item) {
            var outPct = clampHalf(item.OutflowBarPct);
            var inPct = clampHalf(item.InflowBarPct);

            return '<div class="vas-234-bar" aria-hidden="true">' +
                (outPct > 0 ? '<i class="vas-234-bar-out" style="width:' + outPct.toFixed(1) + '%"></i>' : '') +
                (inPct > 0 ? '<i class="vas-234-bar-in" style="width:' + inPct.toFixed(1) + '%"></i>' : '') +
                '<i class="vas-234-axis"></i>' +
            '</div>';
        }

        function clampHalf(value) {
            var v = Number(value) || 0;
            if (v < 0) { return 0; }
            return v > 50 ? 50 : v;
        }

        /* Everything the row holds, in full precision - the compact figures above are
           rounded, and this is where the exact numbers live. */
        function rowTooltip(item) {
            var lines = [];

            lines.push(item.AccountName || '');
            if (item.BankName && item.BankName !== item.AccountName) { lines.push(item.BankName); }
            if (metaText(item)) { lines.push(metaText(item)); }

            lines.push(label('VAS_234_Outflow', 'Outflow') + ': ' +
                amountText(item, item.Outflow) + countSuffix(item.OutflowCount));
            lines.push(label('VAS_234_Inflow', 'Inflow') + ': ' +
                amountText(item, item.Inflow) + countSuffix(item.InflowCount));
            lines.push(label('VAS_234_Net', 'Net') + ': ' + signedAmountText(item, item.Net));

            lines.push(label('VAS_234_ClosingBalance', 'Closing balance') + ': ' +
                amountText(item, item.ClosingBalance));

            return lines.join('\n');
        }

        /* ---- Footer: "Showing a–b of N accounts" left, prev / "Page n of m" / next
             right. Hidden entirely on a single page. ---- */
        function paintFooter() {
            if (_totalPages <= 1) { $foot.empty(); return; }

            var from = (_page - 1) * _pageSize + 1;
            var to = Math.min(_page * _pageSize, _totalRows);

            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _totalRows + ' ' +
                label('VAS_234_Accounts', 'accounts');
            var pageText = label('VAS_234_Page', 'Page') + ' ' + _page + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _totalPages;

            var prevDis = _page <= 1 ? ' disabled' : '';
            var nextDis = _page >= _totalPages ? ' disabled' : '';

            $foot.html(
                '<span class="vas-234-count">' + escapeHtml(showing) + '</span>' +
                '<div class="vas-234-pager">' +
                    '<button type="button" class="vas-234-pgbtn vas-234-pg-prev" aria-label="' +
                        escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                    '<span class="vas-234-page">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas-234-pgbtn vas-234-pg-next" aria-label="' +
                        escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                '</div>'
            );

            /* A page change keeps the sort and the period - only the page number moves. */
            $foot.find('.vas-234-pg-prev').on('click', function () {
                if (!_loading && _page > 1) { fetchPage(_page - 1); }
            });
            $foot.find('.vas-234-pg-next').on('click', function () {
                if (!_loading && _page < _totalPages) { fetchPage(_page + 1); }
            });
        }

        /* ------------------------------------------------------------ */
        /* Adaptive row capacity                                        */
        /* ------------------------------------------------------------ */

        /* The page is five rows by specification. This only ever REDUCES it, and only when
           five genuinely will not fit the cell - §No Inner Scrollbars means a short cell
           has to page more, never clip. Place the widget in a taller cell to see all five. */
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

            /* Size off the TALLEST rendered row so a wrapped account name never clips. */
            var rendered = $list[0].querySelectorAll('.vas-234-brow');
            var maxH = 0;
            for (var i = 0; i < rendered.length; i++) {
                if (rendered[i].offsetHeight > maxH) { maxH = rendered[i].offsetHeight; }
            }
            if (maxH > 0) { _rowH = maxH; }
            var rowH = _rowH > 0 ? _rowH : ROW_HEIGHT_FALLBACK;

            _needsSync = false;

            var capacity = Math.floor(avail / rowH);
            if (capacity < MIN_PAGE_SIZE) { capacity = MIN_PAGE_SIZE; }
            if (capacity > PAGE_SIZE) { capacity = PAGE_SIZE; }

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
        /* Sort picker - anchored under the control, appended to <body>  */
        /* ------------------------------------------------------------ */
        function buildPicker() {
            $picker = $('<div class="vas-234-pp vas-234-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_234_SortBy', 'Sort by')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-234-pp-opt', function () {
                var key = $(this).attr('data-key');
                closePicker();
                selectSort(key);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-234-pp-h">' +
                escapeHtml(label('VAS_234_SortBy', 'Sort by')) + '</div>';

            for (var i = 0; i < SORTS.length; i++) {
                var s = SORTS[i];
                var selected = s.key === _sort;

                html += '<button type="button" class="vas-234-pp-opt" role="option" data-key="' + s.key +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-234-pp-name">' + escapeHtml(label(s.msg, s.text)) + '</span>' +
                    '<span class="vas-234-pp-tick">' + ICONS.tick + '</span>' +
                '</button>';
            }

            $picker.html(html);
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the control if
           something re-anchors it. The dashboard scrolls in its own container, not the
           window, and scroll events do not bubble - a CAPTURE listener on document is the
           only one that sees every scroll, whichever container moved. Scrolling is not a
           dismissal: the panel travels with the control and closes only on a pick, an
           outside click or Escape. */
        var _pickerW = 0;
        var _pickerH = 0;

        function measurePicker() {
            $picker.css('max-height', '');
            _pickerW = $picker.outerWidth();
            _pickerH = $picker.outerHeight();
        }

        function positionPicker() {
            if (!$picker || !$sortBtn || !$sortBtn[0]) { return; }

            var rect = $sortBtn[0].getBoundingClientRect();
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
            /* Right-aligned to the control: it sits at the card's trailing edge, so a
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
            $picker.removeClass('vas-234-hidden');
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
            if ($picker) { $picker.addClass('vas-234-hidden'); }

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
            if ($sortBtn[0] && $sortBtn[0].contains(e.target)) { return; }
            closePicker();
        }

        function onPickerKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closePicker(); }
        }

        /* A sort change goes back to page 1 - the row the user was looking at is not on
           the same page under a different order - and keeps the reporting period. */
        function selectSort(key) {
            if (!key || key === _sort) { return; }

            _sort = key;
            paintSortLabel();
            fetchPage(1);
        }

        /* ------------------------------------------------------------ */
        /* Zoom                                                         */
        /* ------------------------------------------------------------ */
        function zoomToAccount(accountId) {
            if (accountId <= 0 || _zoomWindowId <= 0) { return; }
            if (!VAS.ZoomUtil || typeof VAS.ZoomUtil.zoomToRecord !== 'function') { return; }

            VAS.ZoomUtil.zoomToRecord(ZOOM_COLUMN, accountId, _zoomWindowId,
                ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD);
        }

        /* ------------------------------------------------------------ */
        /* Amount formatting - per ROW currency, shared helper only      */
        /* ------------------------------------------------------------ */
        function precisionOf(item) {
            var p = item ? Number(item.Precision) : NaN;
            return (isNaN(p) || p < 0 || p > 6) ? 2 : p;
        }

        function symbolOf(item) {
            return (item && item.CurrencySymbol) ? item.CurrencySymbol : '';
        }

        /* Compact magnitude (K / L / Cr, or K / M / B) from the shared util, scaled by the
           ROW's own currency - the ISO code drives whether it steps in lakh/crore or
           million/billion, so no tenant currency is assumed anywhere. */
        function compact(item, value) {
            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    return VIS.Util.formatCompactAmount(value,
                        (item && item.CurrencyCode) ? item.CurrencyCode : '', precisionOf(item));
                }
            }
            catch (e) { if (window.console) { console.log(e); } }
            return String(Math.abs(Number(value) || 0));
        }

        /* The closing balance is the row's one absolute figure, and the only place the
           currency symbol appears. */
        function money(item, value) {
            return symbolOf(item) + compact(item, value);
        }

        /* The three flow figures are bare magnitudes: they are all in the currency the row
           already states under the account name, and repeating the symbol three times
           across one row costs width the cell does not have. */
        function flow(item, value) {
            return compact(item, value);
        }

        /* An explicit sign on the net - the sign IS the reading, so it is never left to be
           inferred from the colour alone. A true minus sign, not a hyphen. */
        function signedFlow(item, value) {
            var v = Number(value) || 0;
            if (v === 0) { return compact(item, 0); }
            return (v < 0 ? '−' : '+') + compact(item, v);
        }

        /* Full, non-compact amount for the tooltips: the exact figure behind the rounded
           cell. Grouping and the decimal separator come from the browser locale, the
           decimals from the ROW currency's precision. */
        function amountText(item, value) {
            var abs = Math.abs(Number(value) || 0);
            var p = precisionOf(item);
            var text = abs.toLocaleString(window.navigator.language,
                { minimumFractionDigits: p, maximumFractionDigits: p });
            return (Number(value) < 0 ? '−' : '') + symbolOf(item) + text;
        }

        function signedAmountText(item, value) {
            var v = Number(value) || 0;
            return (v < 0 ? '' : '+') + amountText(item, v);
        }

        function countSuffix(count) {
            var n = Number(count) || 0;
            return n > 0 ? ' (' + n + ')' : '';
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* Every database-sourced string - account name, bank name, currency text, the
           masked number - goes through here before it reaches the DOM. */
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
            if ($sortBtn) { $sortBtn.off(_ns); }
            if ($list) { $list.off(_ns); }
            if ($foot) { $foot.off(); }

            _rows = [];
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_234_AccountBalanceWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_234_AccountBalanceWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_234_AccountBalanceWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
