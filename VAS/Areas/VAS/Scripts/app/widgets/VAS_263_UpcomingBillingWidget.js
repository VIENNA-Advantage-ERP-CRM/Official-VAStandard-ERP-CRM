/**
 * VAS_263 Upcoming Billing Widget (Service Contracts dashboard)
 * Purpose - c5 x r3 CENTERPIECE list widget: uninvoiced C_ContractSchedule
 *           periods (C_Invoice_ID IS NULL) of live contracts whose period
 *           start falls in the window overdue-25d..due-60d, most-overdue
 *           first - an operational, actionable billing queue, not a report.
 *           Header sub-line = "N periods due <=60d - $total -
 *           C_ContractSchedule"; the header "All ->" link opens the full
 *           upcoming-billing list modal. Each row shows a leading receipt
 *           tile (danger tint if overdue, primary if due-soon), customer
 *           (title), DocumentNo / FrequencyLabel / overdue-or-due-in text
 *           (meta), the base-currency next-invoice amount, and an Invoice
 *           button. Row -> the shared contract detail modal (reusing
 *           VAS_241_ContractSearchWidget's GetContract / RunRenew /
 *           RunGenerateInvoice endpoints, the same cross-widget endpoint-reuse
 *           pattern VAS_120 set with VAS_126 and VAS_244/245/246/258/259/261/
 *           262 already reused too), whose "Generate invoice" (and the
 *           row's own Invoice button) run the real CreateContractInvoice
 *           process and "Open record" zooms to the Service Contract window.
 *           The widget never resizes on paging - fixed c5 x r3 footprint,
 *           outer overflow hidden, a full page shows all 7 rows with no
 *           clipping.
 * Design  - upcoming-billing.html (the parent Service Contracts dashboard
 *           mock, single-widget preview of upcomingBillingWidget()) is the
 *           pixel-level source of truth; re-created verbatim below (tokens,
 *           leading receipt icon tile, header icon well/title/sub/"All ->"
 *           link, row structure, pager).
 * Scope   - "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly this widget's upcoming-
 *           billing population, fetched as a plain Contract_ID list
 *           (GetUpcomingBillingContractIds) rather than reconstructed as a
 *           raw DAYSBETWEEN/CURRENT_DATE where fragment client-side (this
 *           codebase's own VAS_140 widget shows that inlining relative date
 *           arithmetic into a raw where-clause breaks across Postgres/
 *           Oracle). When hosted (windowNo >= 0) this goes through
 *           widgetFirevalueChanged / ActionName - the same channel "Open
 *           record" already uses - resolved by NAME through the host window
 *           framework (VAS_244/245/246/258/259/261/262's own openInBrowser
 *           fix, 2026-09-08: VAS.ZoomUtil's hardcoded AD_Window_ID 1000248
 *           turned out to resolve to a different window (Lead) on the real
 *           install). "Live" reads Processed instead of DocStatus, the same
 *           fix already applied to VAS_243/244/245/246/258/259/260/261/262
 *           (see the controller's own header note). The per-row Invoice
 *           button posts to VAS_241's RunGenerateInvoice with the row's
 *           ContractId (not a separate schedule-scoped write path) - the
 *           underlying CreateContractInvoice process already picks up
 *           whichever period(s) are due for that contract.
 *
 * Backend - VAS_263_UpcomingBillingWidget/GetUpcomingBillingSummary     (GET -> DuePeriodCount, DueAmountBase, currency)
 *           VAS_263_UpcomingBillingWidget/GetUpcomingBillingContracts   (GET offset,limit -> rows + total)
 *           VAS_263_UpcomingBillingWidget/GetUpcomingBillingContractIds (GET -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract                    (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                       (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice             (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Upcoming Billing                      | VAS_263_Title
 *  2  | {n} periods due ≤60d                  | VAS_263_SubCount
 *  3  | C_ContractSchedule                    | VAS_263_SubSuffix
 *  4  | All                                   | VAS_263_All
 *  5  | Couldn't load                         | VAS_263_LoadError
 *  6  | Upcoming Billing (list title)         | VAS_263_ListTitle
 *  7  | contracts                             | VAS_263_ContractsCount
 *  8  | Contract                              | VAS_263_ColContract
 *  9  | Value                                 | VAS_263_ColValue
 * 10  | Ends                                  | VAS_263_ColEnds
 * 11  | Renewal                               | VAS_263_ColRenewal
 * 12  | Status                                | VAS_263_ColStatus
 * 13  | of                                    | VAS_263_Of
 * 14  | Previous page                         | VAS_263_PrevPage
 * 15  | Next page                             | VAS_263_NextPage
 * 16  | Nothing here right now.               | VAS_263_Empty
 * 17  | Unable to load contracts.             | VAS_263_UnableToLoad
 * 18  | Close                                 | VAS_263_Close
 * 19  | Open in browser                       | VAS_263_OpenInBrowser
 * 20  | Service Contract                      | VAS_263_ContractTitle
 * 21  | Open record                           | VAS_263_OpenRecord
 * 22  | rep                                   | VAS_263_Rep
 * 23  | cycles                                | VAS_263_Cycles
 * 24  | Contract cycle value                  | VAS_263_Value
 * 25  | Status                                | VAS_263_Status
 * 26  | Type                                  | VAS_263_Type
 * 27  | Renewal                               | VAS_263_Renewal
 * 28  | Ends                                  | VAS_263_Ends
 * 29  | in {n}d                               | VAS_263_EndsIn
 * 30  | Ended {n}d ago                        | VAS_263_EndedAgo
 * 31  | Notice days                           | VAS_263_NoticeDays
 * 32  | Billed amount                         | VAS_263_Billed
 * 33  | Unbilled amount                       | VAS_263_Unbilled
 * 34  | What needs attention                  | VAS_263_NeedsAttention
 * 35  | No open actions - contract is healthy.| VAS_263_NoOpenActions
 * 36  | Renewal notice overdue                | VAS_263_RenewalNoticeOverdue
 * 37  | Renewal notice due today              | VAS_263_RenewalNoticeDueToday
 * 38  | Manual renewal                        | VAS_263_ManualRenewal
 * 39  | notice {n}d overdue                   | VAS_263_NoticeOverdueBy
 * 40  | ends                                  | VAS_263_EndsPrefix
 * 41  | Billing overdue                       | VAS_263_BillingOverdue
 * 42  | Next period {n}d overdue              | VAS_263_NextPeriodOverdue
 * 43  | unbilled total                        | VAS_263_UnbilledTotal
 * 44  | open tickets for this customer        | VAS_263_OpenTickets
 * 45  | Billing schedule                      | VAS_263_BillingSchedule
 * 46  | Period                                | VAS_263_Period
 * 47  | Invoiced                              | VAS_263_Invoiced
 * 48  | Pending                               | VAS_263_SchedulePending
 * 49  | Generate invoice                      | VAS_263_GenerateInvoice
 * 50  | Renew                                 | VAS_263_Renew
 * 51  | Run RenewContract for this contract now? | VAS_263_ConfirmRenew
 * 52  | Generate invoices for the overdue billing periods now? | VAS_263_ConfirmGenerateInvoice
 * 53  | Working…                              | VAS_263_Working
 * 54  | The action failed.                    | VAS_263_ActionFailed
 * 55  | Couldn't load this contract.          | VAS_263_DetailLoadError
 * 56  | d (days-to-end suffix)                | VAS_263_DaysSuffix
 * 57  | overdue {n}d                          | VAS_263_OverdueBy
 * 58  | due in {n}d                           | VAS_263_DueIn
 * 59  | next invoice                          | VAS_263_NextInvoiceCaption
 * 60  | Invoice                               | VAS_263_InvoiceBtn
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_263_UpcomingBillingWidget/';

    var BODY_PAGE = 7;
    var LIST_PAGE = 7;

    function label(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return t && t.charAt(0) !== '[' ? t : fallback;
    }

    function escapeHtml(value) {
        if (value == null) { return ''; }
        return String(value).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function parseResponse(response) {
        var parsed = response;
        if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
        if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
        return parsed || {};
    }

    function formatCount(value) {
        var n = Number(value || 0);
        if (!isFinite(n)) { n = 0; }
        return Math.round(n).toLocaleString(window.navigator.language);
    }

    function fmtCompact(value, precision) {
        var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
        var p = Number(precision); if (isNaN(p) || p < 0) { p = 2; }
        var abs = Math.abs(n);
        var sign = n < 0 ? '-' : '';
        if (abs >= 1e9) { return sign + (abs / 1e9).toFixed(2) + 'B'; }
        if (abs >= 1e6) { return sign + (abs / 1e6).toFixed(2) + 'M'; }
        if (abs >= 1e3) { return sign + (abs / 1e3).toFixed(2) + 'K'; }
        return sign + abs.toFixed(p);
    }

    function formatMoney(value, iso, symbol, precision) {
        var tag = symbol || iso || '';
        if (window.VIS && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '';
            return sign + tag + VIS.Util.formatCompactAmount(Math.abs(n), iso || '', precision);
        }
        return tag + fmtCompact(value, precision);
    }

    function endsText(days) {
        var d = Number(days || 0);
        if (d < 0) { return label('VAS_263_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_263_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    function isManualRenewal(code) {
        var c = String(code || '').toUpperCase();
        return c === 'M' || c === 'MNL';
    }

    // Row meta "overdue Nd" / "due in Nd" (upcoming-billing.prompt.md §4).
    function dueText(daysToInvoice) {
        var d = Number(daysToInvoice || 0);
        if (d < 0) { return label('VAS_263_OverdueBy', 'overdue {n}d').replace('{n}', formatCount(-d)); }
        return label('VAS_263_DueIn', 'due in {n}d').replace('{n}', formatCount(d));
    }

    // Leading receipt tile - danger tint if overdue, primary if due-soon.
    function receiptClass(daysToInvoice) {
        return Number(daysToInvoice || 0) < 0 ? 'vas263-ic-danger' : 'vas263-ic-primary';
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas263-band-ok'; }
        if (code === 'EXPIRING') { return 'vas263-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas263-band-danger'; }
        return 'vas263-band-info';
    }

    var AVATAR_COLORS = ['#1F83FF', '#5F4AA6', '#0B6B45', '#D78B10', '#0083DA', '#A33F3F'];
    function avatarColor(text) {
        var hash = 0;
        var value = String(text || '');
        for (var i = 0; i < value.length; i++) { hash = (hash * 31 + value.charCodeAt(i)) % AVATAR_COLORS.length; }
        return AVATAR_COLORS[hash];
    }
    function initials(name) {
        return String(name || '').split(' ').slice(0, 2).map(function (w) { return w.charAt(0); }).join('').toUpperCase();
    }

    function icon(name) {
        if (name === 'close') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>';
        }
        if (name === 'chev') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>';
        }
        if (name === 'chevL') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>';
        }
        if (name === 'open') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><path d="M15 3h6v6"></path><path d="M10 14 21 3"></path></svg>';
        }
        if (name === 'refresh') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 1 1-2.64-6.36"></path><polyline points="21 3 21 9 15 9"></polyline></svg>';
        }
        if (name === 'invoice') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><path d="M14 2v6h6"></path><path d="M9 13h6M9 17h4"></path></svg>';
        }
        if (name === 'warning') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>';
        }
        if (name === 'ticket') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 9a3 3 0 0 1 0 6v2a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-2a3 3 0 0 1 0-6V7a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2z"></path><path d="M13 5v2M13 11v2M13 17v2"></path></svg>';
        }
        if (name === 'check') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';
        }
        if (name === 'receipt') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1z"></path><path d="M8 7h8M8 11h8M8 15h5"></path></svg>';
        }
        return '';
    }

    // Populates --dash-inline-size (a shared, dashboard-wide CSS var read by
    // .vas263-root's own clamp() font-size formula) from the actual dashboard
    // grid container's width via a page-wide singleton ResizeObserver -
    // without this, the clamp() falls back to 100vw and pegs near its max on
    // any normal desktop window, rendering everything larger than the mock.
    // Same helper ~180 other production widgets in this codebase already use
    // (see VAS_126_OpenTicketsWidget's own copy) - VAS_241/242/243/244/245/
    // 246/258/259/260/261/262/263 were all missing it until now.
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

    VAS.VAS_263_UpcomingBillingWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas263-root">');
        var $body, $bodyList, $bodyPager;
        var bodyOffset = 0, bodyTotal = 0, bodySeq = 0;

        var $list, $listBody, $listPager, $listCount;
        var listOffset = 0, listTotal = 0, listSeq = 0;

        var $detail, $detailBody, $detailMsg;
        var detailBusy = false;

        var $confirm, $confirmMsg;
        var confirmCallback = null;

        var zoomWindowId = ZOOM_WINDOW_ID;

        /* ---------- Widget header + always-visible body list ---------- */

        function createWidget() {
            $root.html(
                '<div class="vas263-head">' +
                    '<div class="vas263-head-l">' +
                        '<span class="vas263-iconwell">' + icon('receipt') + '</span>' +
                        '<span class="vas263-head-text">' +
                            '<span class="vas263-title">' + escapeHtml(label('VAS_263_Title', 'Upcoming Billing')) + '</span>' +
                            '<span class="vas263-sub vas263-skel-sub">&nbsp;</span>' +
                        '</span>' +
                    '</div>' +
                    '<button type="button" class="vas263-alllink" data-open-list>' + escapeHtml(label('VAS_263_All', 'All')) + ' ' + icon('chev') + '</button>' +
                '</div>' +
                '<div class="vas263-body">' +
                    '<div class="vas263-list"></div>' +
                    '<div class="vas263-pager"></div>' +
                '</div>'
            );
            $body = $root.find('.vas263-body');
            $bodyList = $root.find('.vas263-list');
            $bodyPager = $root.find('.vas263-pager');
            $root.on('click', '[data-open-list]', function () { openList(); });
            $root.on('click', '.vas263-row', function () { openDetail(Number($(this).attr('data-cid'))); });
            $root.on('click', '[data-row-invoice]', function (e) { e.stopPropagation(); runRowInvoice(Number($(this).attr('data-cid'))); });
            $root.on('click', '.vas263-pgbtn', function () { turnBodyPage($(this).attr('data-dir')); });
        }

        function subLine(count, valueText) {
            return formatCount(count) + ' ' + label('VAS_263_SubCount', 'periods due ≤60d') + ' · ' + valueText + ' · ' + label('VAS_263_SubSuffix', 'C_ContractSchedule');
        }

        function renderBodyError() {
            $root.find('.vas263-sub').removeClass('vas263-skel-sub').text(label('VAS_263_LoadError', "Couldn't load"));
            $bodyList.html('<div class="vas263-state">' + escapeHtml(label('VAS_263_UnableToLoad', 'Unable to load contracts.')) + '</div>');
            $bodyPager.empty();
        }

        function loadBody() {
            var seq = ++bodySeq;
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetUpcomingBillingContracts',
                type: 'GET', dataType: 'json', cache: false,
                data: { offset: bodyOffset, limit: BODY_PAGE },
                success: function (response) {
                    if (seq !== bodySeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) { renderBodyError(); return; }
                    bodyTotal = Number(parsed.Total || 0);
                    renderBody(parsed.Rows || []);
                    loadSummaryValue();
                },
                error: function () { if (seq === bodySeq) { renderBodyError(); } }
            });
        }

        // Sub-line count/value is a small separate call so paging the body list
        // never re-fetches the (identical) header aggregate.
        function loadSummaryValue() {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetUpcomingBillingSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { return; }
                    var valueText = formatMoney(parsed.DueAmountBase, parsed.CurrencyIso, parsed.CurrencySymbol, parsed.CurrencyPrecision);
                    $root.find('.vas263-sub').removeClass('vas263-skel-sub').text(subLine(parsed.DuePeriodCount, valueText));
                },
                error: function () { /* sub-line just stays as last known value */ }
            });
        }

        function rowHtml(row) {
            var meta = [row.DocumentNo, row.FrequencyLabel, dueText(row.DaysToInvoice)].filter(function (p) { return p; }).join(' · ');
            return '<div class="vas263-row" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas263-ic ' + receiptClass(row.DaysToInvoice) + '">' + icon('receipt') + '</span>' +
                '<span class="vas263-row-main">' +
                    '<span class="vas263-row-name" title="' + escapeHtml(row.CustomerName || '') + '">' + escapeHtml(row.CustomerName || '') + '</span>' +
                    '<span class="vas263-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas263-row-val">' +
                    '<span class="vas263-row-amt">' + escapeHtml(formatMoney(row.NextInvoiceBase, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                    '<span class="vas263-row-vcap">' + escapeHtml(label('VAS_263_NextInvoiceCaption', 'next invoice')) + '</span>' +
                '</span>' +
                '<button type="button" class="vas263-btn vas263-btn-secondary vas263-btn-sm" data-row-invoice data-cid="' + Number(row.ContractId) + '">' + escapeHtml(label('VAS_263_InvoiceBtn', 'Invoice')) + '</button>' +
            '</div>';
        }

        function renderBody(rows) {
            if (!rows.length) {
                $bodyList.html('<div class="vas263-empty">' + icon('check') + '<span>' + escapeHtml(label('VAS_263_Empty', 'Nothing here right now.')) + '</span></div>');
                $bodyPager.empty();
                return;
            }

            $bodyList.html(rows.map(rowHtml).join(''));

            var pages = Math.max(1, Math.ceil(bodyTotal / BODY_PAGE));
            if (pages <= 1) { $bodyPager.empty(); return; }

            var current = Math.floor(bodyOffset / BODY_PAGE);
            var start = bodyOffset + 1, end = bodyOffset + rows.length;
            var of = label('VAS_263_Of', 'of');

            $bodyPager.html(
                '<span class="vas263-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(bodyTotal)) + '</span>' +
                '<span class="vas263-pgctl">' +
                    '<button type="button" class="vas263-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_263_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas263-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_263_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>'
            );
        }

        function turnBodyPage(direction) {
            var next = bodyOffset + (direction === 'next' ? BODY_PAGE : -BODY_PAGE);
            if (next < 0) { next = 0; }
            if (next >= bodyTotal) { return; }
            bodyOffset = next;
            loadBody();
        }

        // Row-level Invoice runs standalone (confirm -> POST -> refresh the row
        // list) - it does NOT open the detail modal first. Routing it through
        // openDetail() used to show both the detail modal and the confirm
        // dialog stacked on top of each other at once (2026-09-09 fix).
        function runRowInvoice(contractId) {
            if (!contractId) { return; }
            showConfirm(label('VAS_263_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?'), function () {
                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'RunGenerateInvoice',
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            if (window.VIS && VIS.ADialog) { VIS.ADialog.error('', false, (parsed && parsed.Message) || label('VAS_263_ActionFailed', 'The action failed.'), ''); }
                            return;
                        }
                        if (window.VIS && VIS.ADialog && parsed.Message) { VIS.ADialog.info(parsed.Message); }
                        loadBody();
                    },
                    error: function () {
                        if (window.VIS && VIS.ADialog) { VIS.ADialog.error('', false, label('VAS_263_ActionFailed', 'The action failed.'), ''); }
                    }
                });
            });
        }

        /* ---------- "All" upcoming-billing list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas263-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas263-scrim" data-list-close></div>' +
                    '<section class="vas263-panel">' +
                        '<header class="vas263-phead">' +
                            '<h2 class="vas263-ptitle">' + escapeHtml(label('VAS_263_ListTitle', 'Upcoming Billing')) + '</h2>' +
                            '<span class="vas263-pcount"></span>' +
                            '<button type="button" class="vas263-close" data-list-close aria-label="' + escapeHtml(label('VAS_263_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas263-colhead">' +
                            '<span>' + escapeHtml(label('VAS_263_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas263-col-r">' + escapeHtml(label('VAS_263_ColValue', 'Value')) + '</span>' +
                            '<span class="vas263-col-r">' + escapeHtml(label('VAS_263_ColEnds', 'Ends')) + '</span>' +
                        '</div>' +
                        '<div class="vas263-pbody"></div>' +
                        '<footer class="vas263-pfoot">' +
                            '<div class="vas263-pager2"></div>' +
                            '<div class="vas263-pfoot-actions">' +
                                '<button type="button" class="vas263-btn vas263-btn-ghost" data-list-close>' + escapeHtml(label('VAS_263_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas263-btn vas263-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_263_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas263-pbody');
            $listPager = $list.find('.vas263-pager2');
            $listCount = $list.find('.vas263-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas263-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas263-pgbtn', function () { turnListPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas263', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function openList() {
            listOffset = 0;
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas263-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas263-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas263-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetUpcomingBillingContracts',
                type: 'GET', dataType: 'json', cache: false,
                data: { offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas263-state">' + escapeHtml(label('VAS_263_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_263_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas263-state">' + escapeHtml(label('VAS_263_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            return '<div class="vas263-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas263-mtc-main">' +
                    '<span class="vas263-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas263-mtc-text">' +
                        '<span class="vas263-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas263-row-meta">' + escapeHtml([row.DocumentNo, row.FrequencyLabel].filter(function (p) { return p; }).join(' · ')) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas263-col-r"><b>' + escapeHtml(formatMoney(row.NextInvoiceBase, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</b></span>' +
                '<span class="vas263-col-r">' + escapeHtml(dueText(row.DaysToInvoice)) + '</span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas263-state">' + escapeHtml(label('VAS_263_Empty', 'Nothing here right now.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_263_Of', 'of');

            $listPager.html(
                '<span class="vas263-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas263-pgctl">' +
                    '<button type="button" class="vas263-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_263_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas263-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_263_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>'
            );
        }

        function turnListPage(direction) {
            var next = listOffset + (direction === 'next' ? LIST_PAGE : -LIST_PAGE);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            listOffset = next;
            loadList();
        }

        // "Open in browser": zooms to the Service Contract window filtered down
        // to exactly this widget's upcoming-billing population, fetched as a
        // plain Contract_ID list (GetUpcomingBillingContractIds) rather than
        // reconstructed as a raw DAYSBETWEEN/CURRENT_DATE where fragment client-
        // side (see the file-header Scope note for why).
        function openInBrowser() {
            closeList();
            try {
                if ($self.windowNo >= 0) {
                    var fire = function (whereClause) {
                        $self.widgetFirevalueChanged({
                            "TabWhereClause": whereClause || "",
                            "TabLayout": "N",
                            "TabIndex": "0",
                            "ActionName": hostWindowName() || CONTRACT_WINDOW_NAME,
                            "ActionType": "W"
                        });
                    };
                    $.ajax({
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetUpcomingBillingContractIds',
                        type: 'GET', dataType: 'json', cache: false,
                        success: function (response) {
                            var parsed = parseResponse(response);
                            var ids = (parsed && parsed.Ids) || [];
                            fire(ids.length ? (ZOOM_TABLE + '.' + ZOOM_TABLE + '_ID IN (' + ids.join(',') + ')') : '');
                        },
                        error: function () { fire(''); }
                    });
                } else if (window.VAS && VAS.ZoomUtil) {
                    VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + '_ID', 0, zoomWindowId, null, null);
                }
            } catch (e) { /* best-effort */ }
        }

        /* ---------- Contract detail modal (VAS_241 endpoints, reused) ---------- */

        function createDetailDialog() {
            $detail = $(
                '<div class="vas263-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas263-scrim" data-detail-close></div>' +
                    '<section class="vas263-panel">' +
                        '<header class="vas263-mhead">' +
                            '<h2 class="vas263-mtitle">' + escapeHtml(label('VAS_263_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas263-mhead-meta"></span>' +
                            '<button type="button" class="vas263-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_263_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas263-mbody"></div>' +
                        '<div class="vas263-mmsg" role="status"></div>' +
                        '<footer class="vas263-mfoot">' +
                            '<button type="button" class="vas263-btn vas263-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_263_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas263-btn vas263-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_263_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas263-btn vas263-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_263_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas263-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_263_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_263_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas263-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas263-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas263-confirm-box">' +
                        '<p class="vas263-confirm-msg"></p>' +
                        '<div class="vas263-confirm-actions">' +
                            '<button type="button" class="vas263-btn vas263-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_263_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas263-btn vas263-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_263_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas263-confirm-msg');
            $confirm.on('click', '[data-confirm-cancel]', closeConfirm);
            $confirm.on('click', '[data-confirm-ok]', function () {
                var callback = confirmCallback;
                closeConfirm();
                if (typeof callback === 'function') { callback(); }
            });
        }

        function showConfirm(message, onConfirm) {
            confirmCallback = onConfirm;
            $confirmMsg.text(message);
            $confirm.addClass('is-open').attr('aria-hidden', 'false');
        }

        function closeConfirm() {
            confirmCallback = null;
            $confirm.removeClass('is-open').attr('aria-hidden', 'true');
        }

        function statHtml(labelKey, fallback, value) {
            return '<div class="vas263-stat">' +
                '<span class="vas263-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas263-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
            '</div>';
        }

        function showDetailMessage(message, isError) {
            if (!message) { $detailMsg.removeClass('is-error is-open').empty(); return; }
            $detailMsg.text(message).toggleClass('is-error', !!isError).addClass('is-open');
        }

        function setDetailBusy(busy) {
            detailBusy = busy;
            $detail.find('[data-detail-renew], [data-detail-invoice]').prop('disabled', busy);
        }

        function openDetail(contractId) {
            if (!contractId) { return; }
            $detail.attr('data-cid', contractId);
            $detailBody = $detail.find('.vas263-mbody');
            $detail.find('.vas263-mhead-meta').empty();
            $detailBody.html('<div class="vas263-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas263-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas263-state">' + escapeHtml(label('VAS_263_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas263-state">' + escapeHtml(label('VAS_263_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            if (isManualRenewal(row.RenewalTypeCode) && !row.HasSuccessor && row.NoticeSlackDays != null && row.NoticeSlackDays <= 0) {
                var noticeTitle = row.NoticeSlackDays < 0
                    ? label('VAS_263_RenewalNoticeOverdue', 'Renewal notice overdue')
                    : label('VAS_263_RenewalNoticeDueToday', 'Renewal notice due today');
                var noticeDescParts = [label('VAS_263_ManualRenewal', 'Manual renewal')];
                if (row.NoticeSlackDays < 0) {
                    noticeDescParts.push(label('VAS_263_NoticeOverdueBy', 'notice {n}d overdue').replace('{n}', formatCount(-row.NoticeSlackDays)));
                }
                noticeDescParts.push(label('VAS_263_EndsPrefix', 'ends') + ' ' + endsText(row.DaysToEnd));
                cards += '<div class="vas263-attn-card">' +
                    '<span class="vas263-attn-ic vas263-attn-ic-danger">' + icon('warning') + '</span>' +
                    '<span class="vas263-attn-main">' +
                        '<span class="vas263-attn-title2">' + escapeHtml(noticeTitle) + '</span>' +
                        '<span class="vas263-attn-desc">' + escapeHtml(noticeDescParts.join(' · ')) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_263_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_263_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas263-attn-card">' +
                    '<span class="vas263-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas263-attn-main">' +
                        '<span class="vas263-attn-title2">' + escapeHtml(label('VAS_263_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas263-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas263-attn-empty">' + escapeHtml(label('VAS_263_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas263-attn">' +
                '<div class="vas263-attn-title">' + escapeHtml(label('VAS_263_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_263_Period', 'Period');
            var invoicedLabel = label('VAS_263_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_263_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas263-band-ok' : 'vas263-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas263-sched-row">' +
                    '<span class="vas263-sched-left">' + left + '</span>' +
                    '<span class="vas263-sched-right">' +
                        '<span class="vas263-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas263-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas263-sched">' +
                '<div class="vas263-sched-title">' + escapeHtml(label('VAS_263_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas263-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas263-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_263_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_263_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas263-dtop2">' +
                    '<span class="vas263-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas263-dhead-main">' +
                        '<span class="vas263-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas263-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas263-dhead-right">' +
                        '<span class="vas263-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas263-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas263-stats">' +
                    statHtml('VAS_263_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_263_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_263_Type', 'Type', row.ContractType) +
                    statHtml('VAS_263_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_263_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_263_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_263_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_263_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                '</div>' +
                attentionCards(row) +
                scheduleHtml(row);

            $detailBody.html(html);
        }

        function runContractAction(endpoint, confirmMessage) {
            if (detailBusy) { return; }
            var contractId = Number($detail.attr('data-cid'));
            if (!contractId) { return; }

            showConfirm(confirmMessage, function () {
                setDetailBusy(true);
                showDetailMessage(label('VAS_263_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_263_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadBody();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_263_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas263-modal-open'); }
        }

        function hostWindowName() {
            try {
                var listener = $self.listener;
                for (var i = 0; i < 6 && listener; i++) {
                    if (listener.apanel && listener.apanel.gridWindow && listener.apanel.gridWindow.getName) { return listener.apanel.gridWindow.getName(); }
                    if (listener.gridWindow && listener.gridWindow.getName) { return listener.gridWindow.getName(); }
                    listener = listener.listener;
                }
            } catch (e) { /* best-effort */ }
            return '';
        }

        function zoomToContract(contractId) {
            if (!contractId) { return; }
            closeDetail();
            closeList();
            try {
                if ($self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": "C_Contract.C_Contract_ID=" + Number(contractId),
                        "TabLayout": "Y", "TabIndex": "0",
                        "ActionName": hostWindowName() || CONTRACT_WINDOW_NAME,
                        "ActionType": "W"
                    });
                } else if (window.VAS && VAS.ZoomUtil) {
                    VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + '_ID', Number(contractId), zoomWindowId, null, null);
                }
            } catch (e) { /* best-effort */ }
        }

        this.Initalize = function () {
            createWidget();
            createListDialog();
            createDetailDialog();
            createConfirmDialog();
            loadBody();
        };

        this.refreshWidget = function () { loadBody(); };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas263');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas263-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_263_UpcomingBillingWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_263_UpcomingBillingWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_263_UpcomingBillingWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_263_UpcomingBillingWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_263_UpcomingBillingWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_263_UpcomingBillingWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
