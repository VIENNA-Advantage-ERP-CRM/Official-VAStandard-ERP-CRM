/**
 * VAS_202_SuspenseBalancesWidget
 * 3x2 grid widget for the Period Control dashboard.
 *
 * The four CONTROL accounts of the tenant's primary accounting schema that should
 * carry nothing at all, and what is actually sitting on them in ONE open
 * accounting period:
 *
 *   Suspense account              Entries   Balance
 *   ---------------------------------------------------
 *   99100 - Suspense Balancing         3     4,200.00
 *   99200 - Suspense Error             2     1,850.00
 *   99250 - Currency Balancing         4       310.00
 *   99300 - Rounding Off               5       120.00
 *
 * Clicking a row opens a paged list of the postings behind it. Each posting names
 * the SCREEN it came from and the source document's own number, and the document
 * number opens that record in its own standard window - the window comes from
 * Fact_Acct.AD_Window_ID, so the widget can be dropped on any dashboard and still
 * land on the right screen.
 *
 * All four rows are always shown, configured or not: these are control-account
 * SETTINGS, and a missing one is itself the finding. An unconfigured row reads
 * "Not configured" and does not drill through; a configured row with nothing on it
 * reads 0 and stays visible, because a suspense account at zero is the good news.
 *
 * Amounts are already stated in the primary accounting schema's currency (Fact_Acct
 * AmtAcctDr / AmtAcctCr), so nothing is converted anywhere - client or server.
 *
 * The footer total is the sum of the ABSOLUTE balances of the DISTINCT accounts: a
 * debit suspense and a credit suspense are two things to investigate, not one that
 * cancels out, and an account named by two settings holds one balance.
 *
 * Period source: the open STANDARD periods of the tenant's primary calendar - a
 * period qualifies when at least one active C_PeriodControl row of it is Open,
 * because suspense postings are not confined to one document base type. Nothing is
 * derived from the calendar month, and Fact_Acct is bounded by C_Period_ID.
 *
 * Sizing follows design.md -> dashboard-widgets.md: the card carries the widget
 * root anchor clamp, the header reads --dash-inline-size (populated by
 * ensureDashInlineSizeVar), the account grid shares one column template between its
 * header and its rows, and nothing scrolls. Narrow cells are handled by container
 * queries, the body-level modal and picker by media queries (see the stylesheet).
 *
 * Summary Message Table
 * Rows marked (reuse) already exist in the project under another key and are NOT
 * duplicated here. Column captions prefer the framework's own translated element
 * name (VIS.translatedTexts[<ColumnName>]) and only fall back to the key.
 *  # | Current Text                                  | Message Key
 * ---+-----------------------------------------------+------------------------------
 *  1 | Suspense Balances                             | VAS_202_SuspenseBalances
 *  2 | Click an account for postings                 | VAS_202_ClickAccountHint
 *  3 | Suspense Account                              | VAS_202_SuspenseAccount
 *  4 | Entries                                       | VAS_202_Entries
 *  5 | Balance                                       | VAS_202_Balance
 *  6 | Suspense Balancing                            | VAS_202_SuspenseBalancing
 *  7 | Suspense Error                                | VAS_202_SuspenseError
 *  8 | Currency Balancing                            | VAS_202_CurrencyBalancing
 *  9 | Rounding Off                                  | VAS_202_RoundingOff
 * 10 | Total suspense to clear                       | VAS_202_TotalToClear
 * 11 | Not configured                                | VAS_202_NotConfigured
 * 12 | Postings                                      | VAS_202_Postings
 * 13 | Screen                                        | VAS_202_Screen
 * 14 | Document Date                                 | VAS_202_DocumentDate
 * 15 | Account Date                                  | VAS_202_AccountDate
 * 16 | Dr / Cr                                       | VAS_202_DrCr
 * 17 | Debit                                         | VAS_202_Debit
 * 18 | Credit                                        | VAS_202_Credit
 * 19 | Amount                                        | VAS_202_Amount
 * 20 | No postings in this period                    | VAS_202_NoPostings
 * 21 | Primary calendar not configured               | VAS_202_NoCalendar
 * 22 | Primary accounting schema not configured      | VAS_202_NoAcctSchema
 * 23 | Suspense account not configured               | VAS_202_AccountNotConfigured
 * 24 | Account combination cannot be resolved        | VAS_202_AccountUnresolved
 * 25 | Same account is configured more than once     | VAS_202_DuplicateAccount
 * 26 | Source record no longer exists                | VAS_202_RecordMissing
 * 27 | Document No                                   | DocumentNo             (reuse)
 * 28 | No open accounting period                     | VAS_201_NoOpenPeriod   (reuse)
 * 29 | Dashboard period                              | VAS_201_DashboardPeriod(reuse)
 * 30 | Close                                         | VAS_018_Close          (reuse)
 * 31 | Couldn't load                                 | VAS_192_CouldntLoad    (reuse)
 * 32 | Showing                                       | VAS_026_Showing        (reuse)
 * 33 | of                                            | VAS_026_Of             (reuse)
 * 34 | Previous                                      | VAS_026_Prev           (reuse)
 * 35 | Next                                          | VAS_026_Next           (reuse)
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

    /* The four C_AcctSchema_GL settings, in display order. `type` is the server-side
       token; kept in lock-step with VASLogic.Models.VAS_202_SuspenseBalancesModel.
       Currency Balancing follows Suspense Error - it is the same class of finding
       (the schema's UseCurrencyBalancing offset), not an operating account. */
    var ACCOUNT_TYPES = [
        { type: 'SuspenseBalancing', key: 'VAS_202_SuspenseBalancing', text: 'Suspense Balancing' },
        { type: 'SuspenseError', key: 'VAS_202_SuspenseError', text: 'Suspense Error' },
        { type: 'CurrencyBalancing', key: 'VAS_202_CurrencyBalancing', text: 'Currency Balancing' },
        { type: 'RoundingOff', key: 'VAS_202_RoundingOff', text: 'Rounding Off' }
    ];

    /* Server error tokens -> the message that explains them. */
    var ERROR_LABELS = {
        NOCALENDAR: { key: 'VAS_202_NoCalendar', text: 'Primary calendar not configured.' },
        NOACCTSCHEMA: { key: 'VAS_202_NoAcctSchema', text: 'Primary accounting schema not configured.' },
        NOPERIOD: { key: 'VAS_201_NoOpenPeriod', text: 'No open accounting period.' },
        INVALID: { key: 'VAS_192_CouldntLoad', text: "Couldn't load" }
    };

    var MODAL_PAGE_SIZE = 8;

    /* Starting estimate for the detail grid's row / header heights, in px. It only
       has to hold until the first page is painted: the real heights are then measured
       and the body grown to fit a full page exactly, because the painted height
       depends on the host's font scale. */
    var MODAL_ROW_H = 58;
    var MODAL_HEAD_H = 52;
    var MODAL_BODY_SLACK = 4;

    VAS.VAS_202_SuspenseBalancesWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-202-root">');
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
        var _ns = '.vas202_' + (VAS.VAS_202_SuspenseBalancesWidget._seq =
            (VAS.VAS_202_SuspenseBalancesWidget._seq || 0) + 1);

        /* Current selection and the data painted from it. */
        var _periods = [];
        var _periodId = 0;
        var _periodName = '';
        var _data = null;
        var _schema = null;

        /* Detail modal state. _detailSeq drops the response of a page the user has
           already navigated away from (or of a period they have already changed). */
        var _accountId = 0;
        var _accountLabel = '';
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
            $busy.toggleClass('vas-202-hidden', _busyCount === 0);
        }

        function showError(text) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) { VIS.ADialog.error("", "", text); }
        }

        /* Counts are never blank: an account with no postings reads 0, not nothing. */
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

        /* Card money: the shared compact formatter, so a seven-figure suspense balance
           does not overrun the row and the numbering system follows the currency (lakh
           / crore vs K / M). The symbol is composed here, never hard-coded. */
        function formatCompact(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            var p = precision();

            var sign = n < 0 ? '-' : '';
            var magnitude = (VIS.Util && VIS.Util.formatCompactAmount)
                ? VIS.Util.formatCompactAmount(n, _schema ? _schema.Iso : '', p)
                : Math.abs(n).toLocaleString(window.navigator.language, {
                    minimumFractionDigits: p, maximumFractionDigits: p
                });

            return sign + symbol() + magnitude;
        }

        /* Modal money: exact, at the schema currency's standard precision - a posting
           list is not a KPI, so nothing is compacted. */
        function formatAmount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            var p = precision();

            var sign = n < 0 ? '-' : '';
            var text = Math.abs(n).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
            return sign + symbol() + text;
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
            /* The widget's own glyph, from the reference design - a warning triangle:
               anything on these accounts is something to clear. */
            if (name === 'suspense') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/><line x1="12" y1="9" x2="12" y2="14"/><line x1="12" y1="17" x2="12" y2="17"/></svg>'; }
            return '';
        }

        // ── Build ────────────────────────────────────────────────────────────

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadBootstrap();
        };

        function createWidget() {
            var title = label('VAS_202_SuspenseBalances', 'Suspense Balances');
            var subtitle = label('VAS_202_ClickAccountHint', 'Click an account for postings');

            var colAccount = label('VAS_202_SuspenseAccount', 'Suspense Account');
            var colEntries = label('VAS_202_Entries', 'Entries');
            var colBalance = label('VAS_202_Balance', 'Balance');

            $card = $(
                '<div class="vas-202-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                    '<div class="vas-202-header">' +
                        '<span class="vas-202-icon">' + icon('suspense') + '</span>' +
                        '<div class="vas-202-head-text">' +
                            '<div class="vas-202-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>' +
                            '<div class="vas-202-subtitle" title="' + escapeHtml(subtitle) + '">' + escapeHtml(subtitle) + '</div>' +
                        '</div>' +
                        /* Period chip: the widget's only filter. It names the period
                           every figure on the card belongs to, and opens the picker. */
                        '<button type="button" class="vas-202-periodchip" aria-haspopup="listbox">' +
                            icon('calendar') +
                            '<span class="vas-202-periodchip-label"></span>' +
                            icon('chevDown') +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-202-body">' +
                        /* Column header and rows share ONE grid template, so the entry
                           and balance columns line up down the card. */
                        '<div class="vas-202-thead">' +
                            '<span class="vas-202-cell" title="' + escapeHtml(colAccount) + '">' +
                                escapeHtml(colAccount) + '</span>' +
                            '<span class="vas-202-cell vas-202-cell-num" title="' + escapeHtml(colEntries) + '">' +
                                escapeHtml(colEntries) + '</span>' +
                            '<span class="vas-202-cell vas-202-cell-num" title="' + escapeHtml(colBalance) + '">' +
                                escapeHtml(colBalance) + '</span>' +
                            '<span class="vas-202-cell-chev"></span>' +
                        '</div>' +
                        '<div class="vas-202-list"></div>' +
                        '<div class="vas-202-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $list = $card.find('.vas-202-list');
            $foot = $card.find('.vas-202-foot');
            $periodBtn = $card.find('.vas-202-periodchip');

            $periodBtn.on('click', function (e) {
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated so the handlers survive every repaint of the list. Only rows
               that resolved to a real account are given the role/tabindex that make
               them activatable, so an unconfigured row cannot be opened by keyboard
               either. */
            $list.on('click', '.vas-202-row-open', function () {
                openModal(parseInt($(this).attr('data-account'), 10) || 0, this);
            });
            $list.on('keydown', '.vas-202-row-open', function (e) {
                if (e.key === 'Enter' || e.keyCode === 13 || e.key === ' ' || e.keyCode === 32) {
                    e.preventDefault();
                    $(this).trigger('click');
                }
            });

            $root.append($card);

            $busy = $('<div class="vas-202-busy vas-202-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

        // ── Loads ────────────────────────────────────────────────────────────

        function loadBootstrap() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_202_SuspenseBalancesWidget/GetBootstrap',
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
                complete: function () { showBusy(false); }
            });
        }

        function loadPeriodData(periodId) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_202_SuspenseBalancesWidget/GetPeriodData',
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
                complete: function () { showBusy(false); }
            });
        }

        function applyData(data) {
            _data = data || null;
            if (_data && _data.Schema) { _schema = _data.Schema; }
            paintList();
        }

        // ── Card rendering ───────────────────────────────────────────────────

        function paintPeriod() {
            var text = _periodName || '—';
            $periodBtn.find('.vas-202-periodchip-label').text(text);
            $periodBtn.attr('title', text);
            /* Nothing to pick when the tenant has no open period at all. */
            $periodBtn.toggleClass('vas-202-hidden', _periods.length === 0);
        }

        function renderState(text, isError) {
            if (!$list) { return; }
            $list.html('<div class="vas-202-state' + (isError ? ' vas-202-state-error' : '') + '">' +
                escapeHtml(text) + '</div>');
            $foot.empty();
        }

        /* The row's own label: the account as an accountant reads it, code first. An
           unconfigured slot has no account to name, so the logical setting name carries
           the row on its own. */
        function accountText(account, def) {
            var value = account ? (account.AccountValue || '') : '';
            var name = account ? (account.AccountName || '') : '';

            if (value && name) { return value + ' - ' + name; }
            if (value) { return value; }
            if (name) { return name; }
            return label(def.key, def.text);
        }

        function paintList() {
            if (!$list) { return; }

            var accounts = (_data && _data.Accounts) ? _data.Accounts : [];
            var html = '';

            for (var i = 0; i < ACCOUNT_TYPES.length; i++) {
                var def = ACCOUNT_TYPES[i];
                var account = findAccount(accounts, def.type);
                html += buildRow(def, account);
            }

            $list.html(html);
            paintFooter();
        }

        function buildRow(def, account) {
            var setting = label(def.key, def.text);
            var main = accountText(account, def);
            var open = !!(account && account.IsConfigured && account.Account_ID > 0);

            /* Only an openable row advertises itself as one: no role, no tabindex and
               no chevron on a row there is nothing behind. */
            var attrs = open
                ? ' class="vas-202-row vas-202-row-open" role="button" tabindex="0" data-account="' +
                  account.Account_ID + '" aria-label="' + escapeHtml(setting + ' · ' + main) + '"'
                : ' class="vas-202-row vas-202-row-off"';

            var entries;
            var balance;

            if (open) {
                entries = formatCount(account.EntryCount);
                /* Zero is the target state, so it is deliberately NOT tinted like a
                   balance that still has to be cleared. */
                var tone = Number(account.Balance || 0) === 0 ? 'vas-202-tone-ok' : 'vas-202-tone-fail';
                balance = '<span class="vas-202-cell vas-202-cell-num vas-202-cell-value ' + tone +
                    '" title="' + escapeHtml(formatAmount(account.Balance)) + '">' +
                    escapeHtml(formatCompact(account.Balance)) + '</span>';
                entries = '<span class="vas-202-cell vas-202-cell-num" title="' + escapeHtml(entries) +
                    '">' + escapeHtml(entries) + '</span>';
            } else {
                /* A control account that is not set up is a setup finding, not a zero -
                   so the row says so instead of printing a figure that would read as
                   "nothing to clear". */
                var missing = label('VAS_202_NotConfigured', 'Not configured');
                entries = '<span class="vas-202-cell vas-202-cell-num vas-202-cell-off" title="' +
                    escapeHtml(missing) + '">—</span>';
                balance = '<span class="vas-202-cell vas-202-cell-num vas-202-cell-off" title="' +
                    escapeHtml(missing) + '">' + escapeHtml(missing) + '</span>';
            }

            return '<div' + attrs + '>' +
                '<span class="vas-202-cell vas-202-cell-name">' +
                    '<span class="vas-202-row-main" title="' + escapeHtml(main) + '">' +
                        escapeHtml(main) + '</span>' +
                    '<span class="vas-202-row-sub" title="' + escapeHtml(setting) + '">' +
                        escapeHtml(setting) + '</span>' +
                '</span>' +
                entries +
                balance +
                '<span class="vas-202-cell-chev">' + (open ? icon('chevR') : '') + '</span>' +
            '</div>';
        }

        /* Footer: the money still to clear, and - when the setup itself is the problem -
           what is wrong with it. The total is the server's, never re-summed here: it is
           the sum of ABSOLUTE balances over DISTINCT accounts, which a client-side sum
           over the four painted rows would get wrong on both counts. */
        function paintFooter() {
            if (!_data) { $foot.empty(); return; }

            var caption = label('VAS_202_TotalToClear', 'Total suspense to clear');
            var total = formatCompact(_data.TotalSuspenseToClear);
            var text = caption + ' · ' + total;
            var exact = caption + ' · ' + formatAmount(_data.TotalSuspenseToClear);

            var warning = firstWarningText();

            $foot.html(
                '<span class="vas-202-foot-info" title="' + escapeHtml(exact) + '">' +
                    escapeHtml(text) + '</span>' +
                (warning
                    ? '<span class="vas-202-foot-warn" title="' + escapeHtml(warning) + '">' +
                          escapeHtml(warning) + '</span>'
                    : '')
            );
        }

        /* One warning is enough in a card footer - the most serious first. A broken
           combination outranks a missing setting (something WAS configured and has
           since stopped resolving), and both outrank a duplicate. */
        function firstWarningText() {
            var warnings = (_data && _data.Warnings) ? _data.Warnings : [];
            if (warnings.length === 0) { return ''; }

            var order = ['UNRESOLVED', 'NOTCONFIGURED', 'DUPLICATE'];
            var texts = {
                UNRESOLVED: label('VAS_202_AccountUnresolved', 'Account combination cannot be resolved'),
                NOTCONFIGURED: label('VAS_202_AccountNotConfigured', 'Suspense account not configured'),
                DUPLICATE: label('VAS_202_DuplicateAccount', 'Same account is configured more than once')
            };

            for (var o = 0; o < order.length; o++) {
                for (var w = 0; w < warnings.length; w++) {
                    if (warnings[w].Code === order[o]) { return texts[order[o]]; }
                }
            }

            return '';
        }

        function findAccount(accounts, type) {
            for (var i = 0; i < accounts.length; i++) {
                if (accounts[i].AccountType === type) { return accounts[i]; }
            }
            return null;
        }

        // ── Period picker ────────────────────────────────────────────────────

        /* Anchored under the chip and appended to <body>. Non-modal, and unlike the
           detail modal it closes on an outside click - it is a menu, not a dialog. */
        function buildPicker() {
            $picker = $('<div class="vas-202-pp vas-202-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '">');
            $('body').append($picker);

            $picker.on('click', '.vas-202-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectPeriod(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-202-pp-h">' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '</div>';

            for (var i = 0; i < _periods.length; i++) {
                var p = _periods[i];
                var selected = p.C_Period_ID === _periodId;
                var meta = p.FiscalYear ? String(p.FiscalYear) : '';

                html += '<button type="button" class="vas-202-pp-opt" role="option" data-id="' + p.C_Period_ID +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-202-pp-name" title="' + escapeHtml(p.Name || '') + '">' +
                        escapeHtml(p.Name || '') + '</span>' +
                    (meta ? '<span class="vas-202-pp-meta">' + escapeHtml(meta) + '</span>' : '') +
                    '<span class="vas-202-pp-tick">' + icon('tick') + '</span>' +
                '</button>';
            }

            $picker.html(html);
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the chip if
           something re-anchors it. The dashboard scrolls in its own container, not the
           window, and scroll events do not bubble - a capture listener on document is
           the only one that sees every scroll, whichever container moved. Scrolling is
           not a dismissal: the panel travels with the chip, off the top or bottom of
           the screen included, and closes only on a pick, an outside click or Escape.

           Panel size is measured once at opening - it cannot change while the user
           scrolls, and re-measuring on every scroll event would thrash layout. */
        var _pickerW = 0;
        var _pickerH = 0;

        function measurePicker() {
            $picker.css('max-height', '');
            _pickerW = $picker.outerWidth();
            _pickerH = $picker.outerHeight();
        }

        function positionPicker() {
            if (!$picker || !$periodBtn || !$periodBtn[0]) { return; }

            var rect = $periodBtn[0].getBoundingClientRect();
            var gap = 6;
            var edge = 8;

            var roomBelow = window.innerHeight - rect.bottom - gap - edge;
            var roomAbove = rect.top - gap - edge;

            /* Hangs below the chip by default and flips above only when the list
               plainly fits better there. It is never pushed off the chip to make it
               fit on screen - where the room is short it is capped instead and the
               list scrolls inside itself, so the panel always reads as belonging to
               the period label it was opened from. */
            var below = _pickerH <= roomBelow || roomBelow >= roomAbove;
            var room = below ? roomBelow : roomAbove;

            var ph = _pickerH;
            if (ph > room) {
                /* .vas-202-pp is border-box, so the cap is the outer height. */
                ph = Math.max(140, room);
                $picker.css('max-height', ph + 'px');
            } else {
                $picker.css('max-height', '');
            }

            var top = below ? rect.bottom + gap : rect.top - ph - gap;

            var left = Math.min(rect.left, window.innerWidth - _pickerW - edge);
            left = Math.max(edge, left);

            $picker.css({ left: Math.round(left) + 'px', top: Math.round(top) + 'px' });
        }

        function onAnchorScroll() {
            if (_pickerOpen) { positionPicker(); }
        }

        function openPicker() {
            if (_periods.length === 0) { return; }
            if (!$picker) { buildPicker(); }

            fillPicker();
            $picker.removeClass('vas-202-hidden');
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
            if ($picker) { $picker.addClass('vas-202-hidden'); }

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

            /* Never leave postings of the previous period on screen. */
            closeModal();

            paintPeriod();
            loadPeriodData(periodId);
        }

        // ── Detail modal ─────────────────────────────────────────────────────

        function buildModal() {
            $overlay = $(
                '<div class="vas-202-overlay vas-202-hidden">' +
                    '<div class="vas-202-modal" role="dialog" aria-modal="true" aria-labelledby="' +
                        modalTitleId() + '">' +
                        '<div class="vas-202-modal-head">' +
                            '<span class="vas-202-modal-ico">' + icon('suspense') + '</span>' +
                            '<div class="vas-202-modal-heads">' +
                                '<div class="vas-202-modal-title" id="' + modalTitleId() + '"></div>' +
                                '<div class="vas-202-modal-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-202-modal-close" aria-label="' +
                                escapeHtml(label('VAS_018_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</div>' +
                        '<div class="vas-202-modal-body"></div>' +
                        '<div class="vas-202-modal-foot"></div>' +
                        /* Sits over the panel, not over the body, so the header and the
                           pager stay readable while a page is in flight. */
                        '<div class="vas-202-modal-busy vas-202-hidden">' +
                            '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($overlay);
            $modalBody = $overlay.find('.vas-202-modal-body');
            $modalPager = $overlay.find('.vas-202-modal-foot');
            $modalBusy = $overlay.find('.vas-202-modal-busy');

            /* The body holds exactly one page, so its height is FIXED - not merely
               floored. syncModalHeight() grows it to the measured height of a real full
               page on the first paint. */
            _bodyH = (MODAL_ROW_H * MODAL_PAGE_SIZE) + MODAL_HEAD_H + MODAL_BODY_SLACK;
            $modalBody.css('height', _bodyH + 'px');

            /* The close button, and Escape - deliberately NOT a click on the scrim. This
               dialog is a work list a reader pages through, so a stray click landing off
               the panel is far more likely to be a missed target than a decision to leave,
               and losing the page to it has nothing to undo it with. */
            $overlay.find('.vas-202-modal-close').on('click', closeModal);

            $modalPager.on('click', '.vas-202-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _page = $btn.attr('data-dir') === 'next' ? _page + 1 : Math.max(1, _page - 1);
                loadModalPage();
            });

            /* The document number IS the zoom affordance - it opens the posting's own
               source screen. */
            $modalBody.on('click', '.vas-202-doclink', function (e) {
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
            var seq = ($self.AD_UserHomeWidgetID || $self.windowNo || 0);
            return 'vas202ModalTitle_' + seq + _ns.replace(/[^a-zA-Z0-9_]/g, '');
        }

        function openModal(accountId, rowEl) {
            if (accountId <= 0 || _periodId <= 0) { return; }
            if (!$overlay) { buildModal(); }

            _accountId = accountId;
            _accountLabel = accountLabelFor(accountId);
            _page = 1;
            _returnFocusTo = rowEl || null;

            $overlay.find('.vas-202-modal-title').text(
                _accountLabel + ' · ' + label('VAS_202_Postings', 'Postings'));
            $overlay.find('.vas-202-modal-sub').text(modalSubtitle());

            /* Nothing of the previous account may show through: the body opens empty
               (at its pinned height) with the indicator over it. */
            $modalBody.empty();
            $modalPager.empty();

            $overlay.removeClass('vas-202-hidden');
            _modalOpen = true;
            closePicker();

            $(document).on('keydown' + _ns + 'm', onModalKeyDown);
            $overlay.find('.vas-202-modal-close').focus();

            loadModalPage();
        }

        function accountLabelFor(accountId) {
            var accounts = (_data && _data.Accounts) ? _data.Accounts : [];
            for (var i = 0; i < accounts.length; i++) {
                if (accounts[i].Account_ID !== accountId) { continue; }

                for (var d = 0; d < ACCOUNT_TYPES.length; d++) {
                    if (ACCOUNT_TYPES[d].type === accounts[i].AccountType) {
                        return accountText(accounts[i], ACCOUNT_TYPES[d]);
                    }
                }
            }
            return '';
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
            if ($overlay) { $overlay.addClass('vas-202-hidden'); }
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
            $modalBusy.toggleClass('vas-202-hidden', !show);
        }

        /* One page at a time from the server - the modal never receives the whole set.
           The previous page stays painted underneath the busy indicator, so the panel
           neither blanks nor resizes while the next one arrives. */
        function loadModalPage() {
            var mySeq = ++_detailSeq;
            var periodId = _periodId;
            var accountId = _accountId;

            showModalBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_202_SuspenseBalancesWidget/GetPostings',
                type: 'GET',
                cache: false,
                data: {
                    periodId: periodId,
                    accountId: accountId,
                    pageNo: _page,
                    pageSize: MODAL_PAGE_SIZE
                },
                success: function (res) {
                    /* Stale response: another page, another account, another period, or
                       the modal has been closed since. */
                    if (mySeq !== _detailSeq || periodId !== _periodId || accountId !== _accountId) { return; }

                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }

                    if (!data || data.error) {
                        renderModalState(label('VAS_192_CouldntLoad', "Couldn't load"), true);
                        return;
                    }

                    if (data.ErrorCode) {
                        renderModalState(errorLabel(data.ErrorCode), true);
                        return;
                    }

                    if (data.Schema) { _schema = data.Schema; }

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

        /* Empty / error takeover of the body. The footer keeps its pager row (with the
           controls disabled) rather than collapsing, so the dialog holds the same height
           in every state. */
        function renderModalState(text, isError) {
            $modalBody.html('<div class="vas-202-modal-state' + (isError ? ' vas-202-modal-state-error' : '') +
                '">' + escapeHtml(text) + '</div>');
            renderModalPager({ Total: 0, PageSize: MODAL_PAGE_SIZE, PageNo: 1, Rows: [] });
        }

        /* The six columns of the posting list, in reading order: where it came from,
           what it was, when, which side, and how much. */
        function columns() {
            return [
                label('VAS_202_Screen', 'Screen'),
                colLabel('DocumentNo', 'VAS_202_DocumentNo', 'Document No'),
                label('VAS_202_DocumentDate', 'Document Date'),
                label('VAS_202_AccountDate', 'Account Date'),
                label('VAS_202_DrCr', 'Dr / Cr'),
                label('VAS_202_Amount', 'Amount')
            ];
        }

        /* Which columns are numeric - they right-align. Only the amount. */
        var NUMERIC_COLUMNS = { 5: true };

        function renderModalRows(data) {
            var rows = data.Rows || [];

            if (rows.length === 0) {
                renderModalState(label('VAS_202_NoPostings', 'No postings in this period.'), false);
                return;
            }

            var captions = columns();

            var head = '<div class="vas-202-dhead">';
            for (var c = 0; c < captions.length; c++) {
                head += cell(captions[c], NUMERIC_COLUMNS[c] ? 'vas-202-dcell-num' : '');
            }
            head += '</div>';

            var body = '';
            for (var i = 0; i < rows.length; i++) {
                body += buildDetailRow(rows[i]);
            }

            $modalBody.html('<div class="vas-202-dgrid">' + head + body + '</div>');
            $modalBody.scrollTop(0);

            syncModalHeight(rows.length);
            renderModalPager(data);
        }

        function cell(text, extraClass) {
            return '<span class="vas-202-dcell' + (extraClass ? ' ' + extraClass : '') +
                '" title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        /* A document number rendered as a link. A button, not an anchor: there is no URL
           to follow - the framework opens the window. The server decides whether the row
           is navigable at all (an active window the role may open, a real record, and a
           key column to position by); without that it stays plain text rather than
           offering a dead link. */
        function docCell(row) {
            var text = row.DocumentNo || '';

            if (!row.CanNavigate) {
                /* A record that is gone still shows what Fact_Acct pointed at - the
                   posting is real even when its source has been deleted. */
                var missing = (row.Record_ID > 0 && !row.IsRecordFound)
                    ? label('VAS_202_RecordMissing', 'Source record no longer exists')
                    : text;

                return '<span class="vas-202-dcell vas-202-dcell-b" title="' + escapeHtml(missing) + '">' +
                    escapeHtml(text) + '</span>';
            }

            return '<span class="vas-202-dcell vas-202-dcell-doc">' +
                '<button type="button" class="vas-202-doclink"' +
                    ' data-key="' + escapeHtml(row.KeyColumnName || '') + '"' +
                    ' data-record="' + (row.Record_ID || 0) + '"' +
                    ' data-window="' + (row.AD_Window_ID || 0) + '"' +
                    ' title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</button>' +
            '</span>';
        }

        function buildDetailRow(row) {
            var isDebit = row.DrCr === 'Debit';
            var drcr = isDebit
                ? label('VAS_202_Debit', 'Debit')
                : label('VAS_202_Credit', 'Credit');

            return '<div class="vas-202-drow">' +
                cell(row.ScreenDisplayName || '') +
                docCell(row) +
                cell(formatDate(row.DocumentDate)) +
                cell(formatDate(row.AccountDate)) +
                '<span class="vas-202-dcell">' +
                    '<span class="vas-202-badge vas-202-badge-' + (isDebit ? 'dr' : 'cr') +
                        '" title="' + escapeHtml(drcr) + '">' + escapeHtml(drcr) + '</span>' +
                '</span>' +
                cell(formatAmount(row.Amount), 'vas-202-dcell-num vas-202-dcell-amt') +
            '</div>';
        }

        /* Grows the fixed body to whatever a FULL page actually measures, so the last
           page never introduces a scrollbar the first page did not have. Only ever
           grows: shrinking back on a short page is the fluctuation this prevents. */
        function syncModalHeight(rowCount) {
            if (!$modalBody || !$modalBody[0] || rowCount <= 0) { return; }

            var headEl = $modalBody[0].querySelector('.vas-202-dhead');
            var rowEls = $modalBody[0].querySelectorAll('.vas-202-drow');
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

        /* Canonical Widget Footer Pager (design.md): "Showing a–b of N" left, compact
           prev / "n of m" / next right. */
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
                '<span class="vas-202-pager-info" title="' + escapeHtml(showing) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<span class="vas-202-pager-nav">' +
                    '<button type="button" class="vas-202-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-202-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-202-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>'
            );
        }

        /* Opens the posting's source record in its own standard window.
           The window id comes from Fact_Acct.AD_Window_ID (already confirmed active and
           role-accessible server-side) and the key column from the source table's own
           dictionary metadata - so this widget can sit on any dashboard, far from the
           screen the posting came from, and still land on the right record. No window
           NAME is involved and no AD_Window_ID is ever hard-coded.
           The dialog closes first: the record opens in its own window, and leaving the
           modal over it would hide what was just opened. Degrades silently - a click can
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
            /* Refresh means "start clean": drop the open panels and re-read the period
               list, because a period may have been opened or closed - or a suspense
               account configured - in another window since this card last loaded. */
            closePicker();
            closeModal();

            _periods = [];
            _periodId = 0;
            _periodName = '';
            _data = null;
            _schema = null;

            loadBootstrap();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($list) { $list.off(); }
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

    VAS.VAS_202_SuspenseBalancesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_202_SuspenseBalancesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_202_SuspenseBalancesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_202_SuspenseBalancesWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_202_SuspenseBalancesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
