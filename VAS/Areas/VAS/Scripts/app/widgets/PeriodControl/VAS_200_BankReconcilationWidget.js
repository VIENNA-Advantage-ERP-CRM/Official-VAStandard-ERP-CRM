/**
 * VAS_200_BankReconcilationWidget
 * 6x2 grid widget for the Period Control dashboard.
 *
 * Two views of ONE open accounting period, side by side:
 *
 *   Reconciliation status          | Accounts open for reconciliation
 *   -------------------------------|---------------------------------------
 *   Reconciled            128      | HSBC Operating · ••••4251
 *   Unreconciled            9      |   Last reconciled 30 Apr · 31 days behind
 *   In progress / bounced   2      | Citi Payroll · ••••8840 …
 *
 * Every row on either side opens a paged detail modal of the payments behind it,
 * and each row's document number is a link that opens that payment in its own
 * standard window (an AR receipt and a vendor payment are different screens).
 *
 * Period source: the OPEN periods of the tenant's primary calendar - a period
 * qualifies when at least one active C_PeriodControl row of it is Open. Nothing
 * is derived from the calendar month. "Days behind" is measured against the
 * selected period's END date, never against today.
 *
 * The CARD's figures are in the tenant's base (primary accounting schema)
 * currency, converted server-side at each payment's accounting date - they are
 * totals, so currencies have to be added together. The MODAL is a record list
 * and shows every payment in the currency it was actually made in, unconverted.
 *
 * Sizing follows design.md -> dashboard-widgets.md: the card carries the widget
 * root anchor clamp, the header reads --dash-inline-size (populated by
 * ensureDashInlineSizeVar), row cells sit one step below the title, and neither
 * column scrolls - the accounts list measures its own capacity and pages the
 * rest through the canonical footer pager. Narrow cells are handled by container
 * queries, the body-level modal and picker by media queries (see the stylesheet).
 *
 * Summary Message Table
 * Rows marked (reuse) already exist in the project under another key and are
 * NOT duplicated here. Column captions prefer the framework's own translated
 * element name (VIS.translatedTexts[<ColumnName>]) and only fall back to the key.
 *  # | Current Text                             | Message Key
 * ---+------------------------------------------+---------------------------------
 *  1 | Bank Reconciliation                      | VAS_200_BankReconciliation
 *  2 | Click a row to list the payments         | VAS_200_ClickRowHint
 *  3 | Reconciliation status                    | VAS_200_ReconciliationStatus
 *  4 | Accounts open for reconciliation         | VAS_200_AccountsOpen
 *  5 | Reconciled                               | VAS_200_Reconciled
 *  6 | Unreconciled                             | VAS_200_Unreconciled
 *  7 | In progress / bounced                    | VAS_200_InProgressBounced
 *  8 | period end                               | VAS_200_PeriodEnd
 *  9 | Last reconciled                          | VAS_200_LastReconciled
 * 10 | days behind                              | VAS_200_DaysBehind
 * 11 | up to date                               | VAS_200_UpToDate
 * 12 | Never reconciled                         | VAS_200_NeverReconciled
 * 13 | Current                                  | VAS_200_Current
 * 14 | Behind                                   | VAS_200_Behind
 * 15 | Never                                    | VAS_200_Never
 * 16 | No bank activity in this period          | VAS_200_NoAccounts
 * 17 | No payments in this category             | VAS_200_NoPayments
 * 18 | No open accounting period                | VAS_200_NoOpenPeriod
 * 19 | Dashboard period                         | VAS_200_DashboardPeriod
 * 20 | Bank account                             | VAS_200_BankAccount
 * 22 | Type                                     | VAS_200_Type
 * 23 | Receipt                                  | VAS_200_Receipt
 * 24 | Payment                                  | VAS_200_Payment
 * 25 | Reconciled?                              | VAS_200_ReconciledFlag
 * 26 | Yes                                      | VAS_200_Yes
 * 27 | No                                       | VAS_200_No
 * 28 | Document No                              | DocumentNo                (reuse)
 * 29 | Account Date                             | DateAcct                  (reuse)
 * 30 | Business Partner                         | C_BPartner_ID             (reuse)
 * 31 | Amount                                   | PayAmt                    (reuse)
 * 32 | Close                                    | VAS_018_Close             (reuse)
 * 33 | Couldn't load                            | VAS_192_CouldntLoad       (reuse)
 * 34 | Showing                                  | VAS_026_Showing           (reuse)
 * 35 | of                                       | VAS_026_Of                (reuse)
 * 36 | Previous                                 | VAS_026_Prev              (reuse)
 * 37 | Next                                     | VAS_026_Next              (reuse)
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

    /* The three status buckets, in display order. `code` is the server-side
       category token and `tone` the colour: reconciled reads as healthy, the two
       that need work read as warnings. Kept in lock-step with
       VASLogic.Models.VAS_200_BankReconcilationModel. */
    var BUCKETS = [
        { code: 'RECONCILED', key: 'VAS_200_Reconciled', text: 'Reconciled', tone: 'ok' },
        { code: 'UNRECONCILED', key: 'VAS_200_Unreconciled', text: 'Unreconciled', tone: 'warn' },
        { code: 'INPROGRESS', key: 'VAS_200_InProgressBounced', text: 'In progress / bounced', tone: 'plain' }
    ];

    var CATEGORY_ACCOUNT = 'ACCOUNT';

    /* Presentation thresholds for how far behind an account is. UI only - they
       never change what the figures mean. */
    var DAYS_BEHIND_AMBER = 1;
    var DAYS_BEHIND_WARN = 6;

    /* Standard windows the document-number link opens. Resolved by NAME at runtime
       by VAS.ZoomUtil - an AD_Window_ID differs per environment and is never
       hard-coded. A receipt and a vendor payment are different screens. */
    var ZOOM_WINDOW_RECEIPT = 'VAS_ARReceipt';
    var ZOOM_WINDOW_PAYMENT = 'VAS_APPayment';
    var ZOOM_WINDOW_PAYMENT_OLD = 'Payment';

    var MODAL_PAGE_SIZE = 8;

    /* Starting estimate for the detail grid's row / header heights, in px. It only
       has to hold until the first page is painted: the real heights are then
       measured and the body grown to fit a full page exactly, because the painted
       height depends on the host's font scale. */
    var MODAL_ROW_H = 58;
    var MODAL_HEAD_H = 52;
    var MODAL_BODY_SLACK = 4;

    /* Accounts list: never fewer than two rows even if the cell collapses, and a
       fallback row height for the first paint before one has been measured. */
    var MIN_ACCOUNT_ROWS = 2;
    var ACCOUNT_ROW_FALLBACK = 46;

    VAS.VAS_200_BankReconcilationWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-200-root">');
        var $card;
        var $statusList;
        var $accountList;
        var $accountLabel;
        var $accountPager;
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
        var _ns = '.vas200_' + (VAS.VAS_200_BankReconcilationWidget._seq =
            (VAS.VAS_200_BankReconcilationWidget._seq || 0) + 1);

        /* Current selection and the data painted from it. */
        var _periods = [];
        var _periodId = 0;
        var _periodName = '';
        var _data = null;

        /* Accounts list paging - client-side over the set the server returned, with
           the page size measured from the cell's own height. */
        var _accounts = [];
        var _accPage = 1;
        var _accPageSize = 3;
        var _accRowH = 0;
        var _accNeedsSync = true;
        var _observer = null;

        /* Detail modal state. _detailSeq drops the response of a page the user has
           already navigated away from (or of a period they have already changed). */
        var _category = '';
        var _bankAccountId = 0;
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
            $busy.toggleClass('vas-200-hidden', _busyCount === 0);
        }

        function showError(text) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) { VIS.ADialog.error("", "", text); }
        }

        /* Counts are never blank: an empty bucket reads as 0, not as nothing. */
        function formatCount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return Math.round(n).toLocaleString(window.navigator.language);
        }

        /* Card money: the shared compact formatter, so a seven-figure total does not
           overrun a row and the numbering system follows the currency (lakh / crore
           vs K / M). The symbol is composed here, never hard-coded. */
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

        /* Modal money: exact, at the currency's own precision - a record list is not
           a KPI, so nothing is compacted. */
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

        /* Bank account numbers are shown by their last digits only - the full number
           never needs to be on a dashboard. */
        function maskAccount(accountNo) {
            var text = String(accountNo || '').replace(/\s/g, '');
            if (!text) { return ''; }
            if (text.length <= 4) { return text; }
            return '••••' + text.slice(-4);
        }

        function icon(name) {
            if (name === 'chevR') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 6 15 12 9 18"/></svg>'; }
            if (name === 'chevL') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>'; }
            if (name === 'chevNext') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>'; }
            if (name === 'chevDown') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>'; }
            if (name === 'calendar') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="17" rx="2"/><line x1="3" y1="9" x2="21" y2="9"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="16" y1="2" x2="16" y2="6"/></svg>'; }
            if (name === 'close') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/></svg>'; }
            if (name === 'tick') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>'; }
            /* Bank: the widget's own glyph, from the reference design. */
            if (name === 'bank') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 21h18"/><path d="M5 21V11l7-5 7 5v10"/><path d="M9 21v-6h6v6"/></svg>'; }
            return '';
        }

        // ── Build ────────────────────────────────────────────────────────────

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadBootstrap();
        };

        function createWidget() {
            var title = label('VAS_200_BankReconciliation', 'Bank Reconciliation');
            var subtitle = label('VAS_200_ClickRowHint', 'Click a row to list the payments');
            var statusLabel = label('VAS_200_ReconciliationStatus', 'Reconciliation status');

            $card = $(
                '<div class="vas-200-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                    '<div class="vas-200-header">' +
                        '<span class="vas-200-icon">' + icon('bank') + '</span>' +
                        '<div class="vas-200-head-text">' +
                            '<div class="vas-200-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>' +
                            '<div class="vas-200-subtitle" title="' + escapeHtml(subtitle) + '">' + escapeHtml(subtitle) + '</div>' +
                        '</div>' +
                        /* Period chip: the widget's only filter. It names the period
                           every figure on the card belongs to, and opens the picker. */
                        '<button type="button" class="vas-200-periodchip" aria-haspopup="listbox">' +
                            icon('calendar') +
                            '<span class="vas-200-periodchip-label"></span>' +
                            icon('chevDown') +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-200-body">' +
                        '<div class="vas-200-split">' +
                            '<div class="vas-200-col">' +
                                '<div class="vas-200-seclabel" title="' + escapeHtml(statusLabel) + '">' +
                                    escapeHtml(statusLabel) + '</div>' +
                                '<div class="vas-200-list vas-200-status-list"></div>' +
                            '</div>' +
                            '<span class="vas-200-vr"></span>' +
                            '<div class="vas-200-col">' +
                                '<div class="vas-200-seclabel vas-200-acc-label"></div>' +
                                '<div class="vas-200-list vas-200-account-list"></div>' +
                                '<div class="vas-200-acc-pager"></div>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $statusList = $card.find('.vas-200-status-list');
            $accountList = $card.find('.vas-200-account-list');
            $accountLabel = $card.find('.vas-200-acc-label');
            $accountPager = $card.find('.vas-200-acc-pager');
            $periodBtn = $card.find('.vas-200-periodchip');

            $periodBtn.on('click', function (e) {
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated so the handlers survive every repaint of either list. */
            $statusList.on('click', '.vas-200-row', function () {
                openModal($(this).attr('data-category'), 0);
            });
            $statusList.on('keydown', '.vas-200-row', onRowKeyDown);

            $accountList.on('click', '.vas-200-row', function () {
                openModal(CATEGORY_ACCOUNT, parseInt($(this).attr('data-account-id'), 10) || 0);
            });
            $accountList.on('keydown', '.vas-200-row', onRowKeyDown);

            $accountPager.on('click', '.vas-200-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _accPage += $btn.attr('data-dir') === 'next' ? 1 : -1;
                paintAccounts();
            });

            $root.append($card);

            $busy = $('<div class="vas-200-busy vas-200-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        function onRowKeyDown(e) {
            if (e.key === 'Enter' || e.keyCode === 13 || e.key === ' ' || e.keyCode === 32) {
                e.preventDefault();
                $(this).trigger('click');
            }
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

        this.startObserving = function () { observeAccountList(); };

        // ── Loads ────────────────────────────────────────────────────────────

        function loadBootstrap() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_200_BankReconcilationWidget/GetBootstrap',
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
                        renderState(label('VAS_200_NoOpenPeriod', 'No open accounting period.'), false);
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
                url: VIS.Application.contextUrl + 'VAS_200_BankReconcilationWidget/GetPeriodData',
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
            _accounts = (_data && _data.Accounts) ? _data.Accounts : [];
            _accPage = 1;
            _accNeedsSync = true;

            paintStatus();
            paintAccountLabel();
            paintAccounts();
        }

        // ── Card rendering ───────────────────────────────────────────────────

        function paintPeriod() {
            var text = _periodName || '—';
            $periodBtn.find('.vas-200-periodchip-label').text(text);
            $periodBtn.attr('title', text);
            /* Nothing to pick when the tenant has no open period at all. */
            $periodBtn.toggleClass('vas-200-hidden', _periods.length === 0);
        }

        /* Both columns are taken over by the state, so the card never shows half a
           reading. */
        function renderState(text, isError) {
            if (!$statusList) { return; }
            var html = '<div class="vas-200-state' + (isError ? ' vas-200-state-error' : '') + '">' +
                escapeHtml(text) + '</div>';
            $statusList.html(html);
            $accountList.html(html);
            $accountLabel.text('');
            $accountPager.empty();
        }

        function paintStatus() {
            if (!$statusList) { return; }

            var buckets = (_data && _data.Buckets) ? _data.Buckets : [];
            var symbol = _data ? _data.BaseCurrencySymbol : '';
            var iso = _data ? _data.BaseCurrencyIso : '';
            var precision = _data ? _data.BaseCurrencyPrecision : 2;
            var html = '';

            for (var i = 0; i < BUCKETS.length; i++) {
                var def = BUCKETS[i];
                var bucket = findBucket(buckets, def.code) || { RecordCount: 0, BaseAmount: 0 };

                var name = label(def.key, def.text);
                var count = formatCount(bucket.RecordCount);
                var amount = formatCompact(bucket.BaseAmount, symbol, iso, precision);

                html += '<div class="vas-200-row" role="button" tabindex="0" data-category="' + def.code +
                        '" aria-label="' + escapeHtml(name) + '">' +
                    '<span class="vas-200-cell-main">' +
                        '<span class="vas-200-cell-name" title="' + escapeHtml(name) + '">' +
                            escapeHtml(name) + '</span>' +
                        '<span class="vas-200-cell-meta" title="' + escapeHtml(amount) + '">' +
                            escapeHtml(amount) + '</span>' +
                    '</span>' +
                    '<span class="vas-200-cell-count vas-200-tone-' + def.tone + '" title="' + escapeHtml(count) + '">' +
                        escapeHtml(count) + '</span>' +
                    '<span class="vas-200-rowchev">' + icon('chevR') + '</span>' +
                '</div>';
            }

            $statusList.html(html);
        }

        function findBucket(buckets, code) {
            for (var i = 0; i < buckets.length; i++) {
                if (buckets[i].Category === code) { return buckets[i]; }
            }
            return null;
        }

        /* "Accounts open for reconciliation · period end 31 May 2026" - the date
           every "days behind" on this side is measured against, stated once. */
        function paintAccountLabel() {
            var text = label('VAS_200_AccountsOpen', 'Accounts open for reconciliation');
            var end = _data ? formatDate(_data.PeriodEndDate) : '';
            if (end) { text += ' · ' + label('VAS_200_PeriodEnd', 'period end') + ' ' + end; }

            $accountLabel.text(text).attr('title', text);
        }

        function paintAccounts() {
            if (!$accountList) { return; }

            if (!_accounts || _accounts.length === 0) {
                $accountList.html('<div class="vas-200-state">' +
                    escapeHtml(label('VAS_200_NoAccounts', 'No bank activity in this period.')) + '</div>');
                $accountPager.empty();
                return;
            }

            var totalPages = _accPageSize > 0 ? Math.ceil(_accounts.length / _accPageSize) : 1;
            if (_accPage > totalPages) { _accPage = totalPages; }
            if (_accPage < 1) { _accPage = 1; }

            var from = (_accPage - 1) * _accPageSize;
            var to = Math.min(from + _accPageSize, _accounts.length);

            var html = '';
            for (var i = from; i < to; i++) {
                html += buildAccountRow(_accounts[i]);
            }
            $accountList.html(html);

            $accountPager.html(pagerHtml(_accPage, totalPages, from + 1, to, _accounts.length));

            /* Adapt the row capacity on the first paint after a data/size change
               only - never on manual page navigation. */
            if (_accNeedsSync) { scheduleSync(); }
        }

        function buildAccountRow(account) {
            var bank = account.BankName || '';
            var masked = maskAccount(account.AccountNo);
            var name = masked ? (bank + ' · ' + masked) : bank;

            var days = Number(account.DaysBehind);
            var never = !isFinite(days) || days < 0;

            /* Tone and the short status word come from the same reading of the gap,
               so the colour and the label can never disagree. */
            var tone, statusText, metaText;

            if (never) {
                tone = 'fail';
                statusText = label('VAS_200_Never', 'Never');
                metaText = label('VAS_200_NeverReconciled', 'Never reconciled');
            } else {
                var lastText = formatDate(account.LastReconciledDate);
                metaText = label('VAS_200_LastReconciled', 'Last reconciled') + ' ' + lastText;

                if (days === 0) {
                    tone = 'ok';
                    statusText = label('VAS_200_Current', 'Current');
                    metaText += ' · ' + label('VAS_200_UpToDate', 'up to date');
                } else {
                    tone = days >= DAYS_BEHIND_WARN ? 'fail' : 'warn';
                    statusText = label('VAS_200_Behind', 'Behind');
                    metaText += ' · ' + formatCount(days) + ' ' + label('VAS_200_DaysBehind', 'days behind');
                }
            }

            return '<div class="vas-200-row" role="button" tabindex="0" data-account-id="' +
                    (account.C_BankAccount_ID || 0) + '" aria-label="' + escapeHtml(name) + '">' +
                '<span class="vas-200-cell-main">' +
                    '<span class="vas-200-cell-name" title="' + escapeHtml(name) + '">' +
                        escapeHtml(name) + '</span>' +
                    '<span class="vas-200-cell-meta vas-200-tone-' + tone + '" title="' + escapeHtml(metaText) + '">' +
                        escapeHtml(metaText) + '</span>' +
                '</span>' +
                '<span class="vas-200-cell-status vas-200-tone-' + tone + '" title="' + escapeHtml(statusText) + '">' +
                    escapeHtml(statusText) + '</span>' +
                '<span class="vas-200-rowchev">' + icon('chevR') + '</span>' +
            '</div>';
        }

        /* Canonical Widget Footer Pager (design.md): "Showing a–b of N" left,
           compact prev / "n of m" / next right. Hidden on a single page. */
        function pagerHtml(pageNo, totalPages, from, to, total) {
            if (totalPages <= 1) { return ''; }

            var ofTxt = label('VAS_026_Of', 'of');
            var showing = label('VAS_026_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + ofTxt + ' ' + total;
            var prevDis = pageNo <= 1 ? ' disabled' : '';
            var nextDis = pageNo >= totalPages ? ' disabled' : '';

            return '<span class="vas-200-pager-info" title="' + escapeHtml(showing) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<span class="vas-200-pager-nav">' +
                    '<button type="button" class="vas-200-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-200-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-200-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>';
        }

        // ── Adaptive row count for the accounts list ─────────────────────────

        function scheduleSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        function syncCapacity() {
            if (!$accountList || !$accountList[0]) { return; }
            if (!_accounts || _accounts.length === 0) { return; }

            var avail = $accountList[0].clientHeight;
            if (avail <= 0) {
                if (_accNeedsSync) { scheduleSync(); }     // layout not settled yet - retry
                return;
            }

            /* Size off the tallest rendered row so a wrapped bank name never clips. */
            var painted = $accountList[0].querySelectorAll('.vas-200-row');
            var maxH = 0;
            for (var i = 0; i < painted.length; i++) {
                if (painted[i].offsetHeight > maxH) { maxH = painted[i].offsetHeight; }
            }
            if (maxH > 0) { _accRowH = maxH; }
            var rowH = _accRowH > 0 ? _accRowH : ACCOUNT_ROW_FALLBACK;

            _accNeedsSync = false;
            var capacity = Math.max(MIN_ACCOUNT_ROWS, Math.floor(avail / rowH));
            if (capacity !== _accPageSize) {
                _accPageSize = capacity;
                paintAccounts();
            }
        }

        function observeAccountList() {
            if (typeof ResizeObserver === 'undefined' || !$accountList || !$accountList[0]) { return; }
            if (_observer) { _observer.disconnect(); }
            _observer = new ResizeObserver(function () {
                _accNeedsSync = true;
                syncCapacity();
            });
            _observer.observe($accountList[0]);
        }

        // ── Period picker ────────────────────────────────────────────────────

        /* Anchored under the chip and appended to <body>. Non-modal, and unlike the
           detail modal it closes on an outside click - it is a menu, not a dialog. */
        function buildPicker() {
            $picker = $('<div class="vas-200-pp vas-200-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_200_DashboardPeriod', 'Dashboard period')) + '">');
            $('body').append($picker);

            $picker.on('click', '.vas-200-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectPeriod(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-200-pp-h">' +
                escapeHtml(label('VAS_200_DashboardPeriod', 'Dashboard period')) + '</div>';

            for (var i = 0; i < _periods.length; i++) {
                var p = _periods[i];
                var selected = p.C_Period_ID === _periodId;
                var meta = p.FiscalYear ? String(p.FiscalYear) : '';

                html += '<button type="button" class="vas-200-pp-opt" role="option" data-id="' + p.C_Period_ID +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-200-pp-name" title="' + escapeHtml(p.Name || '') + '">' +
                        escapeHtml(p.Name || '') + '</span>' +
                    (meta ? '<span class="vas-200-pp-meta">' + escapeHtml(meta) + '</span>' : '') +
                    '<span class="vas-200-pp-tick">' + icon('tick') + '</span>' +
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
                /* .vas-200-pp is border-box, so the cap is the outer height. */
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
            $picker.removeClass('vas-200-hidden');
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
            if ($picker) { $picker.addClass('vas-200-hidden'); }

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

            /* Never leave records of the previous period on screen. */
            closeModal();

            paintPeriod();
            loadPeriodData(periodId);
        }

        // ── Detail modal ─────────────────────────────────────────────────────

        function buildModal() {
            $overlay = $(
                '<div class="vas-200-overlay vas-200-hidden">' +
                    '<div class="vas-200-modal" role="dialog" aria-modal="true">' +
                        '<div class="vas-200-modal-head">' +
                            '<span class="vas-200-modal-ico">' + icon('bank') + '</span>' +
                            '<div class="vas-200-modal-heads">' +
                                '<div class="vas-200-modal-title"></div>' +
                                '<div class="vas-200-modal-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-200-modal-close" aria-label="' +
                                escapeHtml(label('VAS_018_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</div>' +
                        '<div class="vas-200-modal-body"></div>' +
                        '<div class="vas-200-modal-foot"></div>' +
                        /* Sits over the panel, not over the body, so the header and
                           the pager stay readable while a page is in flight. */
                        '<div class="vas-200-modal-busy vas-200-hidden">' +
                            '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($overlay);
            $modalBody = $overlay.find('.vas-200-modal-body');
            $modalPager = $overlay.find('.vas-200-modal-foot');
            $modalBusy = $overlay.find('.vas-200-modal-busy');

            /* The body holds exactly one page, so its height is FIXED - not merely
               floored. syncModalHeight() grows it to the measured height of a real
               full page on the first paint. */
            _bodyH = (MODAL_ROW_H * MODAL_PAGE_SIZE) + MODAL_HEAD_H + MODAL_BODY_SLACK;
            $modalBody.css('height', _bodyH + 'px');

            /* The close button, and Escape - deliberately NOT a click on the scrim. This
               dialog is a work list a reader pages through, so a stray click landing off
               the panel is far more likely to be a missed target than a decision to leave,
               and losing the page to it has nothing to undo it with. */
            $overlay.find('.vas-200-modal-close').on('click', closeModal);

            $modalPager.on('click', '.vas-200-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _page = $btn.attr('data-dir') === 'next' ? _page + 1 : Math.max(1, _page - 1);
                loadModalPage();
            });

            /* The document number IS the zoom affordance. */
            $modalBody.on('click', '.vas-200-doclink', function (e) {
                e.stopPropagation();
                var $link = $(this);
                zoomToPayment(parseInt($link.attr('data-id'), 10) || 0,
                    $link.attr('data-receipt') === 'Y');
            });
        }

        function openModal(category, bankAccountId) {
            if (!category || _periodId <= 0) { return; }
            if (!$overlay) { buildModal(); }

            _category = category;
            _bankAccountId = bankAccountId || 0;
            _page = 1;

            $overlay.find('.vas-200-modal-title').text(modalTitle());
            $overlay.find('.vas-200-modal-sub').text(_periodName || '');

            /* Nothing of the previous view may show through: the body opens empty (at
               its pinned height) with the indicator over it. */
            $modalBody.empty();
            $modalPager.empty();

            $overlay.removeClass('vas-200-hidden');
            _modalOpen = true;
            closePicker();

            $(document).on('keydown' + _ns + 'm', onModalKeyDown);

            loadModalPage();
        }

        /* The bucket's own label, or the account's bank and masked number. */
        function modalTitle() {
            if (_category === CATEGORY_ACCOUNT) {
                var account = findAccount(_bankAccountId);
                if (!account) { return ''; }
                var masked = maskAccount(account.AccountNo);
                return masked ? (account.BankName + ' · ' + masked) : (account.BankName || '');
            }

            for (var i = 0; i < BUCKETS.length; i++) {
                if (BUCKETS[i].code === _category) { return label(BUCKETS[i].key, BUCKETS[i].text); }
            }
            return '';
        }

        function findAccount(accountId) {
            for (var i = 0; i < _accounts.length; i++) {
                if (_accounts[i].C_BankAccount_ID === accountId) { return _accounts[i]; }
            }
            return null;
        }

        function closeModal() {
            if (!_modalOpen) { return; }
            _modalOpen = false;
            _detailSeq++;                       // drop any page still in flight
            showModalBusy(false);
            if ($overlay) { $overlay.addClass('vas-200-hidden'); }
            $(document).off('keydown' + _ns + 'm');
        }

        function onModalKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
        }

        function showModalBusy(show) {
            if (!$modalBusy || !$modalBusy[0]) { return; }
            $modalBusy.toggleClass('vas-200-hidden', !show);
        }

        /* One page at a time from the server - the modal never receives the whole
           set. The previous page stays painted underneath the busy indicator, so the
           panel neither blanks nor resizes while the next one arrives. */
        function loadModalPage() {
            var mySeq = ++_detailSeq;
            var periodId = _periodId;

            showModalBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_200_BankReconcilationWidget/GetPayments',
                type: 'GET',
                cache: false,
                data: {
                    periodId: periodId,
                    category: _category,
                    bankAccountId: _bankAccountId,
                    pageNo: _page,
                    pageSize: MODAL_PAGE_SIZE
                },
                success: function (res) {
                    /* Stale response: another page, another category, another period,
                       or the modal has been closed since. */
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
            $modalBody.html('<div class="vas-200-modal-state' + (isError ? ' vas-200-modal-state-error' : '') +
                '">' + escapeHtml(text) + '</div>');
            renderModalPager({ Total: 0, PageSize: MODAL_PAGE_SIZE, PageNo: 1, Rows: [] });
        }

        function renderModalRows(data) {
            var rows = data.Rows || [];

            if (rows.length === 0) {
                renderModalState(label('VAS_200_NoPayments', 'No payments in this category.'), false);
                return;
            }

            /* Six columns either way, so header and rows share one template. Only the
               fifth differs: from a status bucket the useful column is WHICH bank
               account the payment moved through; from one account it is whether that
               payment is reconciled. */
            var isAccount = _category === CATEGORY_ACCOUNT;

            var head = '<div class="vas-200-dhead">' +
                cell(colLabel('DocumentNo', 'VAS_200_DocumentNo', 'Document No')) +
                cell(colLabel('DateAcct', 'VAS_200_DateAcct', 'Account Date')) +
                cell(label('VAS_200_Type', 'Type')) +
                cell(colLabel('C_BPartner_ID', 'VAS_200_BusinessPartner', 'Business Partner')) +
                cell(isAccount ? label('VAS_200_ReconciledFlag', 'Reconciled?')
                               : label('VAS_200_BankAccount', 'Bank account')) +
                cell(colLabel('PayAmt', 'VAS_200_Amount', 'Amount'), 'vas-200-dcell-amt') +
                '</div>';

            var body = '';
            for (var i = 0; i < rows.length; i++) {
                body += buildDetailRow(rows[i], isAccount);
            }

            $modalBody.html('<div class="vas-200-dgrid">' + head + body + '</div>');
            $modalBody.scrollTop(0);

            syncModalHeight(rows.length);
            renderModalPager(data);
        }

        function cell(text, extraClass) {
            return '<span class="vas-200-dcell' + (extraClass ? ' ' + extraClass : '') +
                '" title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        function buildDetailRow(row, isAccount) {
            var docNo = row.DocumentNo || '';
            var paymentId = row.C_Payment_ID || 0;

            var typeText = row.IsReceipt
                ? label('VAS_200_Receipt', 'Receipt')
                : label('VAS_200_Payment', 'Payment');

            var fifth = isAccount
                ? (row.IsReconciled ? label('VAS_200_Yes', 'Yes') : label('VAS_200_No', 'No'))
                : accountText(row);

            /* The payment's OWN currency - a detail list reports what was actually
               paid, not a conversion of it. The card is where the base-currency
               totals live, because there the currencies have to be added up. */
            var amount = formatAmount(row.PayAmt, row.CurrencySymbol, row.CurrencyPrecision);

            /* The document number carries the zoom. A button, not an anchor: there is
               no URL to follow - the framework opens the window. Without an id there
               is nothing to open, so the number stays plain text. */
            var docCell = paymentId > 0
                ? '<span class="vas-200-dcell vas-200-dcell-doc">' +
                      '<button type="button" class="vas-200-doclink" data-id="' + paymentId +
                          '" data-receipt="' + (row.IsReceipt ? 'Y' : 'N') +
                          '" title="' + escapeHtml(docNo) + '">' + escapeHtml(docNo) + '</button>' +
                  '</span>'
                : cell(docNo, 'vas-200-dcell-b');

            return '<div class="vas-200-drow">' +
                docCell +
                cell(formatDate(row.DateAcct)) +
                cell(typeText) +
                cell(row.BusinessPartnerName || '') +
                cell(fifth) +
                cell(amount, 'vas-200-dcell-amt') +
            '</div>';
        }

        function accountText(row) {
            var masked = maskAccount(row.AccountNo);
            var bank = row.BankName || '';
            if (bank && masked) { return bank + ' · ' + masked; }
            return bank || masked;
        }

        /* Grows the fixed body to whatever a FULL page actually measures, so the last
           page never introduces a scrollbar the first page did not have. Only ever
           grows: shrinking back on a short page is the fluctuation this prevents. */
        function syncModalHeight(rowCount) {
            if (!$modalBody || !$modalBody[0] || rowCount <= 0) { return; }

            var headEl = $modalBody[0].querySelector('.vas-200-dhead');
            var rowEls = $modalBody[0].querySelectorAll('.vas-200-drow');
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
                '<span class="vas-200-pager-info" title="' + escapeHtml(showing) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<span class="vas-200-pager-nav">' +
                    '<button type="button" class="vas-200-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-200-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-200-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>'
            );
        }

        /* A receipt and a vendor payment live in different windows, so the row's own
           direction picks the target. The dialog closes first: the record opens in
           its own window, and leaving the modal over it would hide what was just
           opened. Degrades silently - a click can never throw. */
        function zoomToPayment(paymentId, isReceipt) {
            if (paymentId <= 0 || !VAS.ZoomUtil) { return; }

            closeModal();

            try {
                VAS.ZoomUtil.zoomToRecord('C_Payment_ID', paymentId, 0,
                    isReceipt ? ZOOM_WINDOW_RECEIPT : ZOOM_WINDOW_PAYMENT,
                    isReceipt ? '' : ZOOM_WINDOW_PAYMENT_OLD);
            } catch (e) {
                if (window.console) { console.log(e); }
                showError(label('VAS_192_CouldntLoad', "Couldn't load"));
            }
        }

        // ── Framework contract ───────────────────────────────────────────────

        this.refreshWidget = function () {
            /* Refresh means "start clean": drop the open panels and re-read the
               period list, because a period may have been opened or closed in the
               Period Control widgets since this card last loaded. */
            closePicker();
            closeModal();

            _periods = [];
            _periodId = 0;
            _periodName = '';
            _data = null;
            _accounts = [];
            _accPage = 1;
            _accNeedsSync = true;

            loadBootstrap();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (_observer) { _observer.disconnect(); _observer = null; }
            if ($statusList) { $statusList.off(); }
            if ($accountList) { $accountList.off(); }
            if ($accountPager) { $accountPager.off(); }
            if ($periodBtn) { $periodBtn.off(); }

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

    VAS.VAS_200_BankReconcilationWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_200_BankReconcilationWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_200_BankReconcilationWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
        this.startObserving();
    };

    VAS.VAS_200_BankReconcilationWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_200_BankReconcilationWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
