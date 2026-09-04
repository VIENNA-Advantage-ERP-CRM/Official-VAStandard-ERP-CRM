/************************************************************
 * Module Name    : VAS
 * Purpose        : Unreconciled Bank Lines - a 5x2 paginated exception list for the
 *                  Banking dashboard.
 *
 *                  The statement lines the bank has reported and the books have not
 *                  yet matched - the BANK side of the reconciliation gap:
 *
 *                    [!] Unreconciled Bank Lines   [194 open] [ All accounts v ]
 *                        Statement lines pending reconciliation
 *
 *                    Date          Bank Account      Narration      Age   Amount
 *                    19 May 2026   Uco Bank ····9032 NEFT INWARD     2d  +₹4.20L
 *                    12 May 2026   HDFC ····4417     CHQ CLEARING    9d  −₹2.15L
 *                    28 Apr 2026   Axis ····3390     —              23d  −₹6.80L
 *
 *                                              <  1–6 of 194  >
 *
 *                  THE SIGN IS THE DIRECTION HERE. A statement line carries no
 *                  IsReceipt flag - it is the bank's own record - so StmtAmt is shown
 *                  signed as stored: positive is money the bank took in, negative
 *                  money it paid out. That is the opposite convention from the
 *                  C_Payment cards on this dashboard, where direction comes from a
 *                  flag and the amount is a magnitude, and it is deliberate.
 *
 *                  THE AGE CHIP IS AN SLA, NOT A DECORATION: green to 7 days, amber to
 *                  21, red beyond. There is no grey tier - an old line must not fade
 *                  into the background, because the aging tail is the thing this
 *                  widget exists to make obvious.
 *
 *                  Each row is in ITS OWN account's currency, with that currency's
 *                  symbol and StdPrecision - nothing is converted. The widget lists
 *                  individual bank lines rather than summing across accounts, so
 *                  converting would only obscure what the statement actually says.
 *
 *                  The bank-account filter scopes the list AND the header count, so
 *                  the badge can never disagree with the rows beneath it.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, Grid Data Rows, Widget Footer Pager, Content Fit Budget,
 *                  No Inner Scrollbars) supplies the shell, the row grid and the
 *                  pager; the widget specification supplies the five columns, the
 *                  narration fallback and the age-chip thresholds.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                  | Message Key
 *                  ---+-------------------------------+-----------------------------
 *                   1 | Unreconciled Bank Lines       | VAS_238_UnreconciledBankLine
 *                   2 | Statement lines pending       | VAS_238_UnreconciledHint
 *                     |   reconciliation              |
 *                   3 | open                          | VAS_238_Open
 *                   4 | Date                          | VAS_238_Date
 *                   5 | Bank Account                  | VAS_238_BankAccount
 *                   6 | Narration                     | VAS_238_Narration
 *                   7 | Age                           | VAS_238_Age
 *                   8 | Amount                        | VAS_238_Amount
 *                  10 | All accounts                  | VAS_238_AllAccounts
 *                  11 | Bank account                  | VAS_238_BankAccountFilter
 *                  12 | Nothing unreconciled          | VAS_238_NothingOpen
 *                  13 | No bank accounts available    | VAS_238_NoAccounts
 *                  14 | days old                      | VAS_238_DaysOld
 *                  15 | Showing                       | VAS_020_Showing     (reuse)
 *                  16 | of                            | VAS_020_Of          (reuse)
 *                  17 | Previous                      | VAS_020_Prev        (reuse)
 *                  18 | Next                          | VAS_020_Next        (reuse)
 *                  19 | Couldn't load                 | VAS_192_CouldntLoad (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-03
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_238_UnreconciledBankLineWidget.css. All
       classes are namespaced `vas-238-` so they never collide with sibling widgets. */

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
        alert: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"></path>' +
            '<line x1="12" y1="9" x2="12" y2="13"></line>' +
            '<line x1="12" y1="17" x2="12.01" y2="17"></line></svg>',
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

    /* The age chip's SLA bands. No grey tier: an old line must not fade into the
       background, because the aging tail is what the widget exists to surface. */
    var AGE_GREEN_MAX = 7;
    var AGE_AMBER_MAX = 21;

    /* The narration opens the Bank Statement window positioned on THE LINE ITSELF -
       C_BankStatementLine_ID, not the header - so the reader lands on the exact row the
       card named rather than on a statement they then have to search. Resolved by NAME -
       AD_Window_ID differs per environment - and a row only becomes a link once it
       resolves. The name is the one VAS_229 uses. */
    var ZOOM_COLUMN = 'C_BankStatementLine_ID';
    var ZOOM_WINDOW_NAME_NEW = 'VAS_BankStatement';
    var ZOOM_WINDOW_NAME_OLD = 'Bank Statement';

    var DEFAULT_PAGE_SIZE = 6;
    var MIN_PAGE_SIZE = 1;
    var MAX_PAGE_SIZE = 12;
    var ROW_HEIGHT_FALLBACK = 40;

    VAS.VAS_238_UnreconciledBankLineWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $badge;
        var $acctBtn;
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
        var _accounts = [];
        var _accountId = 0;
        var _page = 1;
        var _pageSize = DEFAULT_PAGE_SIZE;
        var _totalRows = 0;
        var _totalPages = 0;

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
            _ns = '.vas238_' + widgetID;

            buildSkeleton();
            createBusyIndicator();
            setupRootObserver();
            resolveZoomWindow();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads. It covers
           EVERY read: the initial load, the Refresh button, a page turn and an account
           change. Created visible so it is already up from the moment the widget mounts. */
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
           re-reads the whole set, lines may have been reconciled since the last load, and
           the page the user was on is not reliably the same page afterwards. The chosen
           ACCOUNT is kept - that is a filter the user set, not a position in a list. */
        this.refreshWidget = function () {
            fetchPage(1);
        };

        /* Resolve the statement window id ONCE, up front. Narrations only render as links
           when it resolves - a link that navigates nowhere is worse than plain text. */
        function resolveZoomWindow() {
            if (!VAS.ZoomUtil || typeof VAS.ZoomUtil.getWindowId !== 'function') { return; }

            VAS.ZoomUtil.getWindowId(ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                .then(function (id) {
                    if (_disposed) { return; }

                    _zoomWindowId = Number(id) || 0;
                    /* This races the first data load; whichever finishes second paints the
                       links, so rows already on screen are repainted here. */
                    if (_zoomWindowId > 0 && !_loading && _rows && _rows.length > 0) {
                        paintRows();
                    }
                });
        }

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-238-root" id="vas-238-root-' + widgetID + '"></div>');

            var title = label('VAS_238_UnreconciledBankLine', 'Unreconciled Bank Lines');
            var subtitle = label('VAS_238_UnreconciledHint', 'Statement lines pending reconciliation');

            $card = $(
                '<div class="vas-238-card">' +
                    '<div class="vas-238-header">' +
                        '<span class="vas-238-icon">' + ICONS.alert + '</span>' +
                        '<div class="vas-238-head-text">' +
                            '<div class="vas-238-title"></div>' +
                            '<div class="vas-238-subtitle"></div>' +
                        '</div>' +
                        /* The badge is a glance-label chip; the filter is the interactive
                           pill. Two tiers of the chip system, never a third. */
                        '<span class="vas-238-badge"></span>' +
                        '<button type="button" class="vas-238-acct" aria-haspopup="listbox">' +
                            '<span class="vas-238-acct-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-238-body" role="table">' +
                        '<div class="vas-238-ghead vas-238-row" role="row"></div>' +
                        '<div class="vas-238-list" role="rowgroup"></div>' +
                        '<div class="vas-238-pagerwrap"></div>' +
                    '</div>' +
                    '<div class="vas-238-state vas-238-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-238-title').text(title).attr('title', title);
            $card.find('.vas-238-subtitle').text(subtitle).attr('title', subtitle);
            $card.find('.vas-238-body').attr('aria-label', title);

            $badge = $card.find('.vas-238-badge');
            $acctBtn = $card.find('.vas-238-acct');
            $list = $card.find('.vas-238-list');
            $foot = $card.find('.vas-238-pagerwrap');
            $state = $card.find('.vas-238-state');

            paintHead();
            paintAccountLabel();

            $acctBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated, so a repaint never has to rebind. */
            $list.on('click' + _ns, '.vas-238-narrlink', function (e) {
                e.preventDefault();
                e.stopPropagation();
                zoomToStatement(this);
            });

            /* An <a> without an href gets no implicit key activation. */
            $list.on('keydown' + _ns, '.vas-238-narrlink', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar' ||
                    e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault();
                    e.stopPropagation();
                    zoomToStatement(this);
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
            showBusyIndicator();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_238_UnreconciledBankLineWidget/GetLines',
                type: 'GET',
                dataType: 'json',
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: { bankAccountId: _accountId, pageNo: pageNo, pageSize: _pageSize },
                success: function (raw) {
                    _loading = false;
                    if (_disposed) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error || !data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    /* The account list comes back with every read, so a newly added account
                       appears on the next refresh without a second endpoint. */
                    _accounts = data.Accounts || [];
                    paintAccountLabel();

                    _rows = data.Rows || [];
                    _page = Number(data.Page) || 1;
                    _pageSize = Number(data.PageSize) || _pageSize;
                    _totalRows = Number(data.TotalRows) || 0;
                    _totalPages = Number(data.TotalPages) || 0;

                    paintBadge();
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

        /* A load failure takes the card over. Having nothing unreconciled does NOT - that
           is handled inside the list, so the header, its badge and the column labels stay
           put. */
        function renderState(text) {
            $card.find('.vas-238-body').addClass('vas-238-hidden');
            $state.removeClass('vas-238-hidden').text(text);
        }

        /* "194 open" - amber, because an open line is an exception rather than an error.
           It counts the WHOLE filtered set, not the page, and it is hidden at zero: a
           badge reading "0 open" is noise next to an empty-state message saying the same
           thing. */
        function paintBadge() {
            if (_totalRows <= 0) { $badge.addClass('vas-238-hidden').text(''); return; }

            var text = _totalRows + ' ' + label('VAS_238_Open', 'open');
            $badge.removeClass('vas-238-hidden').text(text).attr('title', text);
        }

        function paintAccountLabel() {
            var text = accountNameOf(_accountId);
            $acctBtn.find('.vas-238-acct-label').text(text);
            $acctBtn.attr('title', label('VAS_238_BankAccountFilter', 'Bank account') + ': ' + text);
        }

        /* 0 is "All accounts" - the absence of a filter. An id no longer in the list (an
           account deactivated since the selection was made) also falls back to All, so the
           pill can never name something the rows are not filtered by. */
        function accountNameOf(id) {
            if (id > 0) {
                for (var i = 0; i < _accounts.length; i++) {
                    if (Number(_accounts[i].C_BankAccount_ID) === id) {
                        return _accounts[i].Name || '';
                    }
                }
            }
            return label('VAS_238_AllAccounts', 'All accounts');
        }

        /* §Grid Data Rows header: transparent background, Medium, muted, with a divider
           under it. The type scale goes on the CELLS, not on the row - the row sizes its
           tracks in em, and an em resolves against the element's OWN font-size, so a
           font-size here would compute the header's columns smaller than the body's and
           every label would sit over the wrong column. */
        function paintHead() {
            $card.find('.vas-238-ghead').html(
                '<span role="columnheader">' + escapeHtml(label('VAS_238_Date', 'Date')) + '</span>' +
                '<span role="columnheader">' +
                    escapeHtml(label('VAS_238_BankAccount', 'Bank Account')) + '</span>' +
                '<span role="columnheader">' + escapeHtml(label('VAS_238_Narration', 'Narration')) + '</span>' +
                '<span role="columnheader">' + escapeHtml(label('VAS_238_Age', 'Age')) + '</span>' +
                /* Amount is last and right-aligned - the row's conclusion, and the only
                   column a reader scans vertically. */
                '<span class="vas-238-num" role="columnheader">' +
                    escapeHtml(label('VAS_238_Amount', 'Amount')) + '</span>'
            );
        }

        function paintRows() {
            $state.addClass('vas-238-hidden');
            $card.find('.vas-238-body').removeClass('vas-238-hidden');

            if (!_rows || _rows.length === 0) {
                /* Nothing outstanding is GOOD news, not an error - and with no rows there
                   is nothing to page, so the footer goes too. */
                $list.html('<div class="vas-238-empty">' +
                    escapeHtml(label('VAS_238_NothingOpen', 'Nothing unreconciled')) + '</div>');
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

        function rowHtml(item) {
            var amount = Number(item.Amount) || 0;
            /* The SIGN is the direction on a statement line - there is no IsReceipt flag
               to consult, and the printed sign carries the meaning so it never rests on
               colour alone. */
            var amtCls = amount > 0 ? ' vas-238-pos' : (amount < 0 ? ' vas-238-neg' : '');

            var age = Number(item.AgeDays) || 0;
            var ageCls = age <= AGE_GREEN_MAX ? 'vas-238-chip-green'
                : (age <= AGE_AMBER_MAX ? 'vas-238-chip-amber' : 'vas-238-chip-red');

            /* Column order: Date, Bank Account, Narration, Age, Amount. */
            return '<div class="vas-238-brow vas-238-row" role="row" ' +
                        'title="' + escapeHtml(rowTooltip(item)) + '">' +
                '<div class="vas-238-date" role="cell">' +
                    escapeHtml(formatDate(item.LineDate)) + '</div>' +
                '<div class="vas-238-acctname" role="cell">' +
                    escapeHtml(item.BankAccount || '—') + '</div>' +
                '<div class="vas-238-narr" role="cell">' + narrationHtml(item) + '</div>' +
                '<div class="vas-238-agecell" role="cell">' +
                    '<span class="vas-238-chip ' + ageCls + '">' + age + 'd</span>' +
                '</div>' +
                '<div class="vas-238-amt vas-238-num' + amtCls + '" role="cell">' +
                    escapeHtml(signedAmount(item, amount)) + '</div>' +
            '</div>';
        }

        /* The narration opens the statement window on THIS LINE - the id carried here is
           C_BankStatementLine_ID, matching ZOOM_COLUMN, so the zoom lands on the row the
           card named. A row whose statement window did not resolve renders as plain text;
           there is never a link that goes nowhere.

           A line with no Description shows a plain dash rather than a link: there is
           nothing to name, and a linked dash reads as a control by mistake. */
        function narrationHtml(item) {
            if (!item.Narration) { return '<span class="vas-238-nil">—</span>'; }

            var safe = escapeHtml(item.Narration);
            var id = Number(item.C_BankStatementLine_ID) || 0;

            if (id <= 0 || _zoomWindowId <= 0) {
                return '<span title="' + safe + '">' + safe + '</span>';
            }

            return '<a class="vas-238-narrlink" role="link" tabindex="0" title="' + safe + '" ' +
                    'data-id="' + id + '">' + safe + '</a>';
        }

        /* Everything the row holds, at full precision - the cells above are compact. */
        function rowTooltip(item) {
            var lines = [];

            if (item.Narration) { lines.push(item.Narration); }

            lines.push(formatDate(item.LineDate) + ' · ' +
                (Number(item.AgeDays) || 0) + ' ' + label('VAS_238_DaysOld', 'days old'));

            if (item.BankAccount) { lines.push(item.BankAccount); }
            /* The account's own name only when it adds something the masked label does
               not already say. */
            if (item.AccountName && item.AccountName !== item.BankAccount) {
                lines.push(item.AccountName);
            }

            lines.push(label('VAS_238_Amount', 'Amount') + ': ' +
                signedAmountText(item, Number(item.Amount) || 0));

            return lines.join('\n');
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a–b of N" left,
             compact prev / next control right. Hidden on a single page. ---- */
        function paintFooter() {
            if (_totalPages <= 1) { $foot.empty(); return; }

            var from = (_page - 1) * _pageSize + 1;
            var to = Math.min(_page * _pageSize, _totalRows);

            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _totalRows;

            var prevDis = _page <= 1 ? ' disabled' : '';
            var nextDis = _page >= _totalPages ? ' disabled' : '';

            $foot.html(
                '<div class="vas-238-pager">' +
                    '<span class="vas-238-pager-info">' + escapeHtml(showing) + '</span>' +
                    '<div class="vas-238-pager-nav">' +
                        '<button type="button" class="vas-238-pgbtn vas-238-pg-prev" aria-label="' +
                            escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                        '<span class="vas-238-pager-label">' + _page + ' ' +
                            escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _totalPages + '</span>' +
                        '<button type="button" class="vas-238-pgbtn vas-238-pg-next" aria-label="' +
                            escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                    '</div>' +
                '</div>'
            );

            /* A page change keeps the account filter - only the page number moves. */
            $foot.find('.vas-238-pg-prev').on('click', function () {
                if (!_loading && _page > 1) { fetchPage(_page - 1); }
            });
            $foot.find('.vas-238-pg-next').on('click', function () {
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

            /* Size off the TALLEST rendered row so a wrapped narration never clips. */
            var rendered = $list[0].querySelectorAll('.vas-238-brow');
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
        /* Bank account picker - anchored under the pill, on <body>      */
        /* ------------------------------------------------------------ */
        function buildPicker() {
            $picker = $('<div class="vas-238-pp vas-238-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_238_BankAccountFilter', 'Bank account')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-238-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectAccount(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-238-pp-h">' +
                escapeHtml(label('VAS_238_BankAccountFilter', 'Bank account')) + '</div>';

            /* "All accounts" leads the list - it is the default and the way back to it. */
            html += optionHtml(0, label('VAS_238_AllAccounts', 'All accounts'));

            if (_accounts.length === 0) {
                html += '<div class="vas-238-pp-empty">' +
                    escapeHtml(label('VAS_238_NoAccounts', 'No bank accounts available')) + '</div>';
            }

            for (var i = 0; i < _accounts.length; i++) {
                var a = _accounts[i];
                html += optionHtml(Number(a.C_BankAccount_ID) || 0, a.Name || '');
            }

            $picker.html(html);
        }

        function optionHtml(id, text) {
            var selected = id === _accountId;

            return '<button type="button" class="vas-238-pp-opt" role="option" data-id="' + id +
                    '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-238-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-238-pp-tick">' + ICONS.tick + '</span>' +
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
            if (!$picker || !$acctBtn || !$acctBtn[0]) { return; }

            var rect = $acctBtn[0].getBoundingClientRect();
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
            $picker.removeClass('vas-238-hidden');
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
            if ($picker) { $picker.addClass('vas-238-hidden'); }

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
            if ($acctBtn[0] && $acctBtn[0].contains(e.target)) { return; }
            closePicker();
        }

        function onPickerKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closePicker(); }
        }

        /* Changing the account re-reads from page 1 - the row the user was looking at is
           not on the same page of a different filter, and the badge has to be recounted. */
        function selectAccount(id) {
            if (id === _accountId) { return; }

            _accountId = id;
            paintAccountLabel();
            fetchPage(1);
        }

        /* ------------------------------------------------------------ */
        /* Zoom                                                         */
        /* ------------------------------------------------------------ */
        function zoomToStatement(el) {
            if (_zoomWindowId <= 0) { return; }
            if (!VAS.ZoomUtil || typeof VAS.ZoomUtil.zoomToRecord !== 'function') { return; }

            var id = parseInt($(el).attr('data-id'), 10) || 0;
            if (id <= 0) { return; }

            VAS.ZoomUtil.zoomToRecord(ZOOM_COLUMN, id, _zoomWindowId,
                ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD);
        }

        /* ------------------------------------------------------------ */
        /* Formatting - per ROW currency                                */
        /* ------------------------------------------------------------ */
        function precisionOf(item) {
            var p = item ? Number(item.Precision) : NaN;
            return (isNaN(p) || p < 0 || p > 6) ? 2 : p;
        }

        function symbolOf(item) {
            return (item && item.CurrencySymbol) ? item.CurrencySymbol : '';
        }

        /* Compact magnitude with the sign and symbol composed on. The ROW's own currency
           drives the scale - the ISO code decides whether it steps in lakh/crore or
           million/billion - so no tenant currency is assumed anywhere. A true minus sign,
           not a hyphen, and an explicit plus so the direction is never inferred. */
        function signedAmount(item, value) {
            var v = Number(value) || 0;
            var magnitude;

            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    magnitude = VIS.Util.formatCompactAmount(v,
                        (item && item.CurrencyCode) ? item.CurrencyCode : '', precisionOf(item));
                }
            }
            catch (e) { if (window.console) { console.log(e); } }

            if (magnitude === undefined) { magnitude = String(Math.abs(v)); }

            return (v < 0 ? '−' : (v > 0 ? '+' : '')) + symbolOf(item) + magnitude;
        }

        /* Full, non-compact amount for the tooltip: the exact figure behind the compact
           cell. Grouping and the decimal separator come from the browser locale, the
           decimals from the ROW currency's precision. */
        function signedAmountText(item, value) {
            var v = Number(value) || 0;
            var p = precisionOf(item);
            var text = Math.abs(v).toLocaleString(window.navigator.language,
                { minimumFractionDigits: p, maximumFractionDigits: p });

            return (v < 0 ? '−' : (v > 0 ? '+' : '')) + symbolOf(item) + text;
        }

        /* Dates arrive as yyyy-MM-dd and are rendered in the browser's locale, WITH the
           year: the aging tail on this card runs to hundreds of days, so "19 May" alone
           leaves the reader guessing which May. Parsed part by part rather than through
           Date(string), which reads a bare ISO date as UTC and shifts it a day back for
           anyone west of Greenwich. */
        function formatDate(isoDate) {
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

        /* Every database-sourced string - and a bank narration is the most user-supplied
           text on this dashboard - goes through here before it reaches the DOM. */
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
            if ($acctBtn) { $acctBtn.off(_ns); }
            if ($list) { $list.off(_ns); }
            if ($foot) { $foot.off(); }

            _rows = [];
            _accounts = [];
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_238_UnreconciledBankLineWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_238_UnreconciledBankLineWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_238_UnreconciledBankLineWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
