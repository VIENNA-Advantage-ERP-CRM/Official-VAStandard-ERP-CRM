/**
 * VAS_199_PaymentAllocationStatusWidget
 * 3x2 grid widget for the Period Control dashboard.
 *
 * Three mutually exclusive allocation buckets of the completed / closed payments
 * (C_Payment) that fall inside ONE open accounting period, with the record count
 * of each. Clicking a bucket opens a paged detail modal listing those payments;
 * each row's document number is a link that opens that payment in its standard
 * window and closes the dialog behind it.
 *
 *   Bucket                      | Rule (server-side, priority order)
 *   ----------------------------|--------------------------------------------
 *   Allocated (CO / CL)         | IsAllocated = Y
 *   Advances / prepayments      | not allocated AND (IsPrepayment = Y OR the
 *                               | linked C_Charge is an advance charge)
 *   Settlement, not allocated   | everything else that is CO / CL
 *
 * The period comes from the chip in the header: only periods with at least one
 * OPEN C_PeriodControl row are offered, never "the current month", and the
 * selected period's own StartDate / EndDate bound C_Payment.DateAcct. Changing
 * the period reloads all three counts and drops any open detail modal, so stale
 * records are never left on screen.
 *
 * Sizing follows design.md -> dashboard-widgets.md: the card carries the widget
 * root anchor clamp, the header reads --dash-inline-size (populated by
 * ensureDashInlineSizeVar), rows sit one step below the title, and the card
 * never scrolls - the bucket list is fixed at three rows and the paging lives in
 * the modal.
 *
 * Not built on VAS.KpiDrill: that shared modal renders one avatar + title + meta
 * + value per row with a single navigate target, and this detail view is a
 * column grid (document, dates, type, partner, currency, amount) with a
 * per-row Zoom action, so its row model does not carry the payload.
 *
 * Summary Message Table
 * Rows marked (reuse) already exist in the project under another key and are
 * NOT duplicated here. Column captions prefer the framework's own translated
 * element name (VIS.translatedTexts[<ColumnName>]) and only fall back to the key.
 *  # | Current Text                             | Message Key
 * ---+------------------------------------------+---------------------------------
 *  1 | Payment Allocation Status                | VAS_199_PaymentAllocationStatus
 *  2 | Click a status to list the payments      | VAS_199_ClickStatusHint
 *  3 | Settlement, not allocated                | VAS_199_SettlementNotAllocated
 *  4 | Advances / prepayments                   | VAS_199_AdvancesPrepayments
 *  5 | Allocated (CO / CL)                      | VAS_199_AllocatedCoCl
 *  6 | require allocation or reclassification   | VAS_199_NeedsAttention
 *  6a| Open Payment Allocation                  | VAS_199_OpenPaymentAllocation
 *  6b| Oldest unallocated                       | VAS_199_OldestUnallocated
 *  6c| days                                     | VAS_199_Days
 *  6d| Unallocated value                        | VAS_199_UnallocatedValue
 *  7 | No open accounting period                | VAS_199_NoOpenPeriod
 *  8 | Dashboard period                         | VAS_199_DashboardPeriod
 *  9 | No payments in this category             | VAS_199_NoPayments
 * 10 | Document No                              | DocumentNo                (reuse)
 * 11 | Account Date                             | DateAcct                  (reuse)
 * 12 | Type                                     | VAS_199_Type
 * 13 | Business Partner                         | C_BPartner_ID             (reuse)
 * 14 | Currency                                 | C_Currency_ID             (reuse)
 * 15 | Amount                                   | PayAmt                    (reuse)
 * 16 | Close                                    | VAS_018_Close             (reuse)
 * 17 | Couldn't load                            | VAS_192_CouldntLoad       (reuse)
 * 18 | Showing                                  | VAS_026_Showing           (reuse)
 * 19 | of                                       | VAS_026_Of                (reuse)
 * 20 | Previous                                 | VAS_026_Prev              (reuse)
 * 21 | Next                                     | VAS_026_Next              (reuse)
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

    /* The three buckets, in display order. `code` is the server-side category
       token, `field` the count it reads off the counts payload, and `tone` the
       badge colour: the two buckets that need somebody to act are highlighted,
       the settled one reads as healthy. Kept in lock-step with
       VASLogic.Models.VAS_199_PaymentAllocationStatusModel. */
    var CATEGORIES = [
        { code: 'SETTLE', field: 'SettlementCount', key: 'VAS_199_SettlementNotAllocated', text: 'Settlement, not allocated', tone: 'warn' },
        { code: 'ADVANCE', field: 'AdvanceCount', key: 'VAS_199_AdvancesPrepayments', text: 'Advances / prepayments', tone: 'plain' },
        { code: 'ALLOC', field: 'AllocatedCount', key: 'VAS_199_AllocatedCoCl', text: 'Allocated (CO / CL)', tone: 'ok' }
    ];

    /* Standard windows the Zoom action opens. Resolved by NAME at runtime by
       VAS.ZoomUtil - an AD_Window_ID differs per environment and is never
       hard-coded. A receipt and a vendor payment are different screens. */
    var ZOOM_WINDOW_RECEIPT = 'VAS_ARReceipt';
    var ZOOM_WINDOW_PAYMENT = 'VAS_APPayment';
    var ZOOM_WINDOW_PAYMENT_OLD = 'Payment';

    var MODAL_PAGE_SIZE = 8;

    /* Starting estimate for the detail grid's row / header heights, in px. It only
       has to hold until the first page is painted: the real heights are then
       MEASURED and the body is grown to fit a full page exactly, because the
       painted height depends on the host's font scale and cannot be derived from
       the stylesheet. The body height is fixed either way, so a short last page,
       an empty category and the loading state all occupy the same box. */
    var MODAL_ROW_H = 58;
    var MODAL_HEAD_H = 52;

    /* Breathing room under the last row so a rounding pixel cannot summon a
       scrollbar on a page that otherwise fits exactly. */
    var MODAL_BODY_SLACK = 4;

    VAS.VAS_199_PaymentAllocationStatusWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-199-root">');
        var $card;
        var $list;
        var $action;
        var $foot;
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
        var _ns = '.vas199_' + (VAS.VAS_199_PaymentAllocationStatusWidget._seq =
            (VAS.VAS_199_PaymentAllocationStatusWidget._seq || 0) + 1);

        /* Current selection and the counts painted from it. */
        var _periods = [];
        var _periodId = 0;
        var _periodName = '';
        var _counts = null;

        /* Detail modal state. _detailSeq drops the response of a page the user has
           already navigated away from (or of a period they have already changed). */
        var _category = '';
        var _page = 1;
        var _detailSeq = 0;
        var _pickerOpen = false;
        var _modalOpen = false;
        var _busyCount = 0;

        /* Fixed pixel height of the modal body - see syncModalHeight(). */
        var _bodyH = 0;

        // ── Small helpers ────────────────────────────────────────────────────

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[' && translated !== key) ? translated : fallback;
        }

        /* Column captions: prefer the framework's own translated element name so a
           column is captioned exactly as it is everywhere else in the product; the
           AD_Message key is only the fallback. */
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
            $busy.toggleClass('vas-199-hidden', _busyCount === 0);
        }

        function showError(text) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) { VIS.ADialog.error("", "", text); }
        }

        /* Counts are never blank: an empty category reads as 0, not as nothing. */
        function formatCount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return Math.round(n).toLocaleString(window.navigator.language);
        }

        /* Exact amount at the row's OWN currency precision, with that currency's
           symbol - this is a record list, not a KPI, so nothing is compacted and no
           symbol is ever hard-coded. */
        function formatAmount(value, symbol, precision) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            var p = Number(precision);
            if (!isFinite(p) || p < 0) { p = 2; }

            var sign = n < 0 ? '-' : '';
            var text = Math.abs(n).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
            /* Symbol butts against the figure - "₹1,250.00", not "₹ 1,250.00". */
            return sign + (symbol ? symbol : '') + text;
        }

        /* Headline money: the shared compact formatter, so a seven-figure total does
           not overrun the strip and the numbering system follows the base currency
           (lakh / crore vs K / M). The ISO code rides alongside as the unit, so no
           symbol is hard-coded. */
        function formatCompactAmount(value, iso, precision) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            var p = Number(precision);
            if (!isFinite(p) || p < 0) { p = 2; }

            var sign = n < 0 ? '-' : '';
            if (VIS.Util && VIS.Util.formatCompactAmount) {
                return sign + VIS.Util.formatCompactAmount(n, iso, p);
            }
            return sign + Math.abs(n).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        /* Server dates arrive as ISO strings without a zone marker, so they parse
           as local time and no day can shift. */
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
            if (name === 'arrowR') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="18" y2="12"/><polyline points="13 7 18 12 13 17"/></svg>'; }
            if (name === 'pending') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h4"/><line x1="7.5" y1="7.5" x2="13" y2="7.5"/><line x1="7.5" y1="11" x2="11" y2="11"/><circle cx="16.5" cy="16.5" r="4.5" fill="#FFFFFF"/><polyline points="16.5 14.4 16.5 16.7 18.2 17.6"/></svg>'; }
            if (name === 'money') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M8.6 3h6.8l-1.2 3.1a1 1 0 0 1-.93.64h-2.54a1 1 0 0 1-.93-.64L8.6 3z"/><path d="M9.7 6.9C7 8.3 5.2 10.9 5.2 14a6.8 6.8 0 0 0 13.6 0c0-3.1-1.8-5.7-4.5-7.1"/><path d="M12 10.2v7.2"/><path d="M13.9 11.7h-2.5a1.35 1.35 0 0 0 0 2.7h1.2a1.35 1.35 0 0 1 0 2.7h-2.5"/></svg>'; }
            if (name === 'close') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/></svg>'; }
            if (name === 'tick') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>'; }
            if (name === 'zoom') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 3h5v5"/><path d="M21 3l-7 7"/><path d="M8 21H3v-5"/><path d="M3 21l7-7"/></svg>'; }
            return '';
        }

        // ── Build ────────────────────────────────────────────────────────────

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadBootstrap();
        };

        function createWidget() {
            var title = label('VAS_199_PaymentAllocationStatus', 'Payment Allocation Status');
            var subtitle = label('VAS_199_ClickStatusHint', 'Click a status to list the payments');

            $card = $(
                '<div class="vas-199-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                    '<div class="vas-199-header">' +
                        '<span class="vas-199-icon">' + icon('zoom') + '</span>' +
                        '<div class="vas-199-head-text">' +
                            '<div class="vas-199-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>' +
                            '<div class="vas-199-subtitle" title="' + escapeHtml(subtitle) + '">' + escapeHtml(subtitle) + '</div>' +
                        '</div>' +
                        /* Period chip: the widget's only filter. It names the period
                           every count on the card belongs to, and opens the picker. */
                        '<button type="button" class="vas-199-periodchip" aria-haspopup="listbox">' +
                            icon('calendar') +
                            '<span class="vas-199-periodchip-label"></span>' +
                            icon('chevDown') +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-199-body">' +
                        '<div class="vas-199-list"></div>' +
                        '<div class="vas-199-action"></div>' +
                        '<div class="vas-199-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $list = $card.find('.vas-199-list');
            $action = $card.find('.vas-199-action');
            $foot = $card.find('.vas-199-foot');
            $periodBtn = $card.find('.vas-199-periodchip');

            /* Delegated: the strip is repainted on every period change. */
            $action.on('click', '.vas-199-openform', function () { openAllocationForm(); });

            $periodBtn.on('click', function (e) {
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated so the handlers survive every repaint of the list. */
            $list.on('click', '.vas-199-row', function () {
                openModal($(this).attr('data-category'));
            });
            $list.on('keydown', '.vas-199-row', function (e) {
                if (e.key === 'Enter' || e.keyCode === 13 || e.key === ' ' || e.keyCode === 32) {
                    e.preventDefault();
                    openModal($(this).attr('data-category'));
                }
            });

            $root.append($card);

            $busy = $('<div class="vas-199-busy vas-199-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
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

        // ── Loads ────────────────────────────────────────────────────────────

        function loadBootstrap() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_199_PaymentAllocationStatusWidget/GetBootstrap',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

                    _periods = data.Periods || [];
                    _periodId = data.C_Period_ID || 0;
                    _periodName = data.PeriodName || '';
                    _counts = data.Counts || null;

                    paintPeriod();

                    if (_periods.length === 0 || _periodId <= 0) {
                        renderState(label('VAS_199_NoOpenPeriod', 'No open accounting period.'), false);
                        return;
                    }

                    paintList();
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); }
            });
        }

        function loadCounts(periodId) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_199_PaymentAllocationStatusWidget/GetCounts',
                type: 'GET',
                cache: false,
                data: { periodId: periodId },
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

                    /* A late response for a period the user has already moved away
                       from must not overwrite the current counts. */
                    if (periodId !== _periodId) { return; }

                    if (data.ErrorCode) {
                        /* The period stopped qualifying (closed elsewhere while the
                           dashboard was open) - re-read the whole selector. */
                        loadBootstrap();
                        return;
                    }

                    _counts = data;
                    paintList();
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); }
            });
        }

        // ── Card rendering ───────────────────────────────────────────────────

        function paintPeriod() {
            var text = _periodName || '—';
            $periodBtn.find('.vas-199-periodchip-label').text(text);
            $periodBtn.attr('title', text);
            /* Nothing to pick when the tenant has no open period at all. */
            $periodBtn.toggleClass('vas-199-hidden', _periods.length === 0);
        }

        /* State takeover: with no period there is nothing to act on and nothing to
           total, so both strips go with the list. */
        function renderState(text, isError) {
            if (!$list) { return; }
            $list.html('<div class="vas-199-state' + (isError ? ' vas-199-state-error' : '') + '">' +
                escapeHtml(text) + '</div>');
            $action.empty();
            $foot.empty();
        }

        function paintList() {
            if (!$list) { return; }

            var counts = _counts || {};
            var html = '';

            for (var i = 0; i < CATEGORIES.length; i++) {
                var cat = CATEGORIES[i];
                var name = label(cat.key, cat.text);
                var count = formatCount(counts[cat.field]);

                html += '<div class="vas-199-row" role="button" tabindex="0" data-category="' + cat.code +
                        '" aria-label="' + escapeHtml(name) + '">' +
                    '<span class="vas-199-cell-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                    '<span class="vas-199-cell-count vas-199-count-' + cat.tone + '" title="' + escapeHtml(count) + '">' +
                        escapeHtml(count) + '</span>' +
                    '<span class="vas-199-rowchev">' + icon('chevR') + '</span>' +
                '</div>';
            }

            $list.html(html);
            paintAction();
            paintFoot();
        }

        /* Action strip: how many payments still need somebody to act - the two
           unallocated buckets together - and the way to go and do it. The button is
           only offered when the server could resolve the standard Allocation form;
           a dead button would be worse than none. */
        function paintAction() {
            if (!$action) { return; }

            var counts = _counts || {};
            var open = Number(counts.SettlementCount || 0) + Number(counts.AdvanceCount || 0);
            var text = label('VAS_199_NeedsAttention', 'require allocation or reclassification');
            var formId = Number(counts.AllocationFormId || 0);
            var openLabel = label('VAS_199_OpenPaymentAllocation', 'Open Payment Allocation');

            var html = '<div class="vas-199-action-info">' +
                    '<span class="vas-199-action-ico">' + icon('pending') + '</span>' +
                    '<span class="vas-199-action-text" title="' +
                        escapeHtml(formatCount(open) + ' ' + text) + '">' +
                        '<b class="vas-199-action-count">' + escapeHtml(formatCount(open)) + '</b>' +
                        escapeHtml(text) +
                    '</span>' +
                '</div>';

            if (formId > 0) {
                html += '<span class="vas-199-stat-sep"></span>' +
                    '<button type="button" class="vas-199-openform" data-form-id="' + formId +
                        '" title="' + escapeHtml(openLabel) + '">' +
                        escapeHtml(openLabel) + icon('arrowR') +
                    '</button>';
            }

            $action.html(html);
        }

        /* Opens the standard Allocation form. The AD_Form_ID was resolved server-side
           from AD_Form.ClassName, never hard-coded, and startForm keeps the user
           inside the shell - no full-page navigation. */
        function openAllocationForm() {
            var formId = Number((_counts || {}).AllocationFormId || 0);
            if (formId <= 0) { return; }
            if (!VIS.viewManager || typeof VIS.viewManager.startForm !== 'function') { return; }

            closeModal();
            closePicker();

            try {
                VIS.viewManager.startForm(formId);
            } catch (e) {
                if (window.console) { console.log(e); }
            }
        }

        /* Footer strip: the two headline figures of what is still unallocated - how
           long the oldest one has been waiting, and what the whole of it is worth in
           the tenant's base currency. Both are stated as zero / a dash rather than
           hidden when there is nothing outstanding. */
        function paintFoot() {
            var counts = _counts || {};

            var days = Number(counts.OldestUnallocatedDays);
            var hasDays = isFinite(days) && days >= 0;
            var daysText = hasDays ? formatCount(days) : '—';
            var daysTip = hasDays && counts.OldestUnallocatedDate
                ? formatDate(counts.OldestUnallocatedDate)
                : daysText;

            var amountText = formatCompactAmount(counts.UnallocatedAmount,
                counts.BaseCurrencyIso, counts.BaseCurrencyPrecision);
            var iso = counts.BaseCurrencyIso || '';

            $foot.html(
                statHtml(icon('calendar'),
                    label('VAS_199_OldestUnallocated', 'Oldest unallocated'),
                    daysText, label('VAS_199_Days', 'days'), daysTip) +
                '<span class="vas-199-stat-sep"></span>' +
                statHtml(icon('money'),
                    label('VAS_199_UnallocatedValue', 'Unallocated value'),
                    amountText, iso, amountText + (iso ? ' ' + iso : ''))
            );
        }

        function statHtml(iconSvg, labelText, value, unit, tip) {
            return '<div class="vas-199-stat">' +
                '<span class="vas-199-stat-ico">' + iconSvg + '</span>' +
                '<span class="vas-199-stat-text">' +
                    '<span class="vas-199-stat-label" title="' + escapeHtml(labelText) + '">' +
                        escapeHtml(labelText) + '</span>' +
                    '<span class="vas-199-stat-value" title="' + escapeHtml(tip) + '">' +
                        escapeHtml(value) +
                        (unit ? '<span class="vas-199-stat-unit">' + escapeHtml(unit) + '</span>' : '') +
                    '</span>' +
                '</span>' +
            '</div>';
        }

        // ── Period picker ────────────────────────────────────────────────────

        /* Anchored under the chip and appended to <body>. Non-modal, and unlike the
           detail modal it closes on an outside click - it is a menu, not a dialog. */
        function buildPicker() {
            $picker = $('<div class="vas-199-pp vas-199-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_199_DashboardPeriod', 'Dashboard period')) + '">');
            $('body').append($picker);

            $picker.on('click', '.vas-199-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectPeriod(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-199-pp-h">' +
                escapeHtml(label('VAS_199_DashboardPeriod', 'Dashboard period')) + '</div>';

            for (var i = 0; i < _periods.length; i++) {
                var p = _periods[i];
                var selected = p.C_Period_ID === _periodId;
                var meta = p.FiscalYear ? String(p.FiscalYear) : '';

                html += '<button type="button" class="vas-199-pp-opt" role="option" data-id="' + p.C_Period_ID +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-199-pp-name" title="' + escapeHtml(p.Name || '') + '">' +
                        escapeHtml(p.Name || '') + '</span>' +
                    (meta ? '<span class="vas-199-pp-meta">' + escapeHtml(meta) + '</span>' : '') +
                    '<span class="vas-199-pp-tick">' + icon('tick') + '</span>' +
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
            $picker.removeClass('vas-199-hidden');
            positionPicker();
            _pickerOpen = true;

            /* Bound only while the picker is open, under this widget's own
               namespace, so two instances never fight over them. */
            $(document).on('click' + _ns, onDocumentClick);
            $(document).on('keydown' + _ns, onPickerKeyDown);
            $(window).on('resize' + _ns + ' scroll' + _ns, closePicker);
        }

        function closePicker() {
            if (!_pickerOpen) { return; }
            _pickerOpen = false;
            if ($picker) { $picker.addClass('vas-199-hidden'); }

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

            _periodId = periodId;
            _periodName = '';
            for (var i = 0; i < _periods.length; i++) {
                if (_periods[i].C_Period_ID === periodId) { _periodName = _periods[i].Name || ''; break; }
            }

            /* Never leave records of the previous period on screen. */
            closeModal();

            paintPeriod();
            loadCounts(periodId);
        }

        // ── Detail modal ─────────────────────────────────────────────────────

        function buildModal() {
            $overlay = $(
                '<div class="vas-199-overlay vas-199-hidden">' +
                    '<div class="vas-199-modal" role="dialog" aria-modal="true">' +
                        '<div class="vas-199-modal-head">' +
                            /* Same glyph and well as the card header, so the dialog
                               reads as this widget's own surface rather than a
                               generic list. */
                            '<span class="vas-199-modal-ico">' + icon('zoom') + '</span>' +
                            '<div class="vas-199-modal-heads">' +
                                '<div class="vas-199-modal-title"></div>' +
                                '<div class="vas-199-modal-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-199-modal-close" aria-label="' +
                                escapeHtml(label('VAS_018_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</div>' +
                        '<div class="vas-199-modal-body"></div>' +
                        '<div class="vas-199-modal-foot"></div>' +
                        /* Sits over the panel, not over the body, so the header and
                           the pager stay readable while a page is in flight. */
                        '<div class="vas-199-modal-busy vas-199-hidden">' +
                            '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($overlay);
            $modalBody = $overlay.find('.vas-199-modal-body');
            $modalPager = $overlay.find('.vas-199-modal-foot');
            $modalBusy = $overlay.find('.vas-199-modal-busy');

            /* The body holds exactly one page, so its height is FIXED - not merely
               floored. This is the opening estimate; syncModalHeight() grows it to
               the measured height of a real full page on the first paint. Paired
               with scrollbar-gutter in the stylesheet, so the columns do not shift
               if a scrollbar ever does appear. */
            _bodyH = (MODAL_ROW_H * MODAL_PAGE_SIZE) + MODAL_HEAD_H + MODAL_BODY_SLACK;
            $modalBody.css('height', _bodyH + 'px');

            $overlay.find('.vas-199-modal-close').on('click', closeModal);
            /* Scrim click closes; a click inside the panel must not. */
            $overlay.on('click', function (e) { if (e.target === $overlay[0]) { closeModal(); } });

            $modalPager.on('click', '.vas-199-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                var dir = $btn.attr('data-dir');
                _page = dir === 'next' ? _page + 1 : Math.max(1, _page - 1);
                loadModalPage();
            });

            /* The document number IS the zoom affordance - opening the payment in
               its own standard window. Never a full-page navigation, and the window
               id is resolved from the name at runtime. */
            $modalBody.on('click', '.vas-199-doclink', function (e) {
                e.stopPropagation();
                var $link = $(this);
                zoomToPayment(parseInt($link.attr('data-id'), 10) || 0,
                    $link.attr('data-receipt') === 'Y');
            });
        }

        function openModal(category) {
            if (!category || _periodId <= 0) { return; }
            if (!$overlay) { buildModal(); }

            _category = category;
            _page = 1;

            var cat = findCategory(category);
            $overlay.find('.vas-199-modal-title')
                .text(cat ? label(cat.key, cat.text) : '');
            $overlay.find('.vas-199-modal-sub').text(_periodName || '');

            /* Nothing of the previous category may show through: the body opens
               empty (at its pinned height) with the indicator over it. */
            $modalBody.empty();
            $modalPager.empty();

            $overlay.removeClass('vas-199-hidden');
            _modalOpen = true;
            closePicker();

            $(document).on('keydown' + _ns + 'm', onModalKeyDown);

            loadModalPage();
        }

        function closeModal() {
            if (!_modalOpen) { return; }
            _modalOpen = false;
            _detailSeq++;                       // drop any page still in flight
            showModalBusy(false);
            if ($overlay) { $overlay.addClass('vas-199-hidden'); }
            $(document).off('keydown' + _ns + 'm');
        }

        function onModalKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
        }

        function findCategory(code) {
            for (var i = 0; i < CATEGORIES.length; i++) {
                if (CATEGORIES[i].code === code) { return CATEGORIES[i]; }
            }
            return null;
        }

        function showModalBusy(show) {
            if (!$modalBusy || !$modalBusy[0]) { return; }
            $modalBusy.toggleClass('vas-199-hidden', !show);
        }

        /* One page at a time from the server - the modal never receives the whole
           category. The previous page stays painted underneath the busy indicator,
           so the panel neither blanks nor resizes while the next one arrives. */
        function loadModalPage() {
            var mySeq = ++_detailSeq;
            var periodId = _periodId;

            showModalBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_199_PaymentAllocationStatusWidget/GetPayments',
                type: 'GET',
                cache: false,
                data: {
                    periodId: periodId,
                    category: _category,
                    pageNo: _page,
                    pageSize: MODAL_PAGE_SIZE
                },
                success: function (res) {
                    /* Stale response: another page, another category, another
                       period, or the modal has been closed since. */
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
           the controls disabled) rather than collapsing, so the dialog holds the
           same height in every state. */
        function renderModalState(text, isError) {
            $modalBody.html('<div class="vas-199-modal-state' + (isError ? ' vas-199-modal-state-error' : '') +
                '">' + escapeHtml(text) + '</div>');
            renderModalPager({ Total: 0, PageSize: MODAL_PAGE_SIZE, PageNo: 1, Rows: [] });
        }

        function renderModalRows(data) {
            var rows = data.Rows || [];

            if (rows.length === 0) {
                renderModalState(label('VAS_199_NoPayments', 'No payments in this category.'), false);
                return;
            }

            var head = '<div class="vas-199-dhead">' +
                cell(colLabel('DocumentNo', 'VAS_199_DocumentNo', 'Document No')) +
                cell(colLabel('DateAcct', 'VAS_199_DateAcct', 'Account Date')) +
                cell(label('VAS_199_Type', 'Type')) +
                cell(colLabel('C_BPartner_ID', 'VAS_199_BusinessPartner', 'Business Partner')) +
                cell(colLabel('C_Currency_ID', 'VAS_199_Currency', 'Currency')) +
                cell(colLabel('PayAmt', 'VAS_199_Amount', 'Amount'), 'vas-199-dcell-amt') +
                '</div>';

            var body = '';
            for (var i = 0; i < rows.length; i++) {
                body += buildDetailRow(rows[i]);
            }

            $modalBody.html('<div class="vas-199-dgrid">' + head + body + '</div>');
            $modalBody.scrollTop(0);

            syncModalHeight(rows.length);
            renderModalPager(data);
        }

        /* Grows the fixed body to whatever a FULL page actually measures, so the
           last page never introduces a scrollbar the first page did not have. Only
           ever grows: shrinking back on a short page is the very fluctuation this
           is here to prevent, and the height is remembered for the life of the
           dialog. Measured rather than computed - the painted row height follows
           the host's font scale, which the stylesheet cannot know. */
        function syncModalHeight(rowCount) {
            if (!$modalBody || !$modalBody[0] || rowCount <= 0) { return; }

            var headEl = $modalBody[0].querySelector('.vas-199-dhead');
            var rowEls = $modalBody[0].querySelectorAll('.vas-199-drow');
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

        function cell(text, extraClass) {
            return '<span class="vas-199-dcell' + (extraClass ? ' ' + extraClass : '') +
                '" title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        function buildDetailRow(row) {
            /* Type = the payment's own document type (C_DocType.Name) - the name the
               user already knows from the Payment window, not a description this
               widget invents. */
            var typeText = row.DocTypeName || '';
            var amount = formatAmount(row.PayAmt, row.CurrencySymbol, row.CurrencyPrecision);
            var partner = row.BusinessPartnerName || '';
            var iso = row.CurrencyIso || '';
            var docNo = row.DocumentNo || '';
            var paymentId = row.C_Payment_ID || 0;

            /* The document number carries the zoom. A button, not an anchor: there is
               no URL to follow - the framework opens the window - and an href would
               invite a middle-click into a broken tab. Without an id there is nothing
               to open, so the number stays plain text rather than offering a dead
               link. */
            var docCell = paymentId > 0
                ? '<span class="vas-199-dcell vas-199-dcell-doc">' +
                      '<button type="button" class="vas-199-doclink" data-id="' + paymentId +
                          '" data-receipt="' + (row.IsReceipt ? 'Y' : 'N') +
                          '" title="' + escapeHtml(docNo) + '">' + escapeHtml(docNo) + '</button>' +
                  '</span>'
                : cell(docNo, 'vas-199-dcell-b');

            return '<div class="vas-199-drow">' +
                docCell +
                cell(formatDate(row.DateAcct)) +
                cell(typeText) +
                cell(partner) +
                cell(iso) +
                cell(amount, 'vas-199-dcell-amt') +
            '</div>';
        }

        /* Canonical Widget Footer Pager (design.md): "Showing a–b of N" left,
           compact prev / "n of m" / next right. */
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
                '<span class="vas-199-pager-info" title="' + escapeHtml(showing) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<span class="vas-199-pager-nav">' +
                    '<button type="button" class="vas-199-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-199-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-199-pgbtn" data-dir="next" aria-label="' +
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
            _counts = null;

            loadBootstrap();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($list) { $list.off(); }
            if ($action) { $action.off(); }
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
            }

            $root.remove();
        };
    };

    VAS.VAS_199_PaymentAllocationStatusWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_199_PaymentAllocationStatusWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_199_PaymentAllocationStatusWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_199_PaymentAllocationStatusWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_199_PaymentAllocationStatusWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
