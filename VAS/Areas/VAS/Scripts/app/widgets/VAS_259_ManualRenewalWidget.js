/**
 * VAS_259 Manual Renewals — Act Before Notice Widget (Service Contracts dashboard)
 * Purpose - c4 x r3 action-list widget: live, manual-renewal (RenewalType='M')
 *           contracts with no successor that are at or approaching their
 *           notice deadline (daysToEnd <= CancelBeforeDays + 30), most-overdue/
 *           closest-to-notice first - a protect-the-revenue queue (manual
 *           contracts do NOT auto-renew; missing the notice window lapses
 *           them). Header sub-line = "N manual contracts - M past notice
 *           date"; the header "All ->" link opens the full list modal. Each
 *           row shows a leading clock icon (amber, red when overdue), the
 *           customer (title), noticeText / endText / value (meta), a notice
 *           status chip (red Overdue / amber Act soon) and a Renew button.
 *           Row -> the shared contract detail modal (reusing
 *           VAS_241_ContractSearchWidget's GetContract / RunRenew /
 *           RunGenerateInvoice endpoints, the same cross-widget endpoint-reuse
 *           pattern VAS_120 set with VAS_126 and VAS_244/245/246/258 already
 *           reused too), whose "Renew" runs the real RenewContract process and
 *           "Open record" zooms to the Service Contract window. The widget
 *           never resizes on paging - fixed c4 x r3 footprint, outer overflow
 *           hidden, a full page shows all 7 rows with no clipping.
 * Design  - manual-renewals.html (the parent Service Contracts dashboard mock,
 *           single-widget preview of manualRenewalWidget()) is the pixel-level
 *           source of truth; re-created verbatim below (tokens, state-tinted
 *           clock icon tile, notice status chip, header icon well/title/sub/
 *           "All ->" link, row structure, pager).
 * Scope   - "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly this widget's manual-
 *           notice population, fetched as a plain Contract_ID list
 *           (GetManualRenewalContractIds) rather than reconstructed as a raw
 *           correlated-NOT-EXISTS/DAYSBETWEEN where fragment client-side (this
 *           codebase's own VAS_140 widget shows that inlining relative date
 *           arithmetic into a raw where-clause breaks across Postgres/
 *           Oracle). When hosted (windowNo >= 0) this goes through
 *           widgetFirevalueChanged / ActionName - the same channel "Open
 *           record" already uses - resolved by NAME through the host window
 *           framework (VAS_244/245/246/258's own openInBrowser fix,
 *           2026-09-08: VAS.ZoomUtil's hardcoded AD_Window_ID 1000248 turned
 *           out to resolve to a different window (Lead) on the real install).
 *           The header "All ->" reuses the same manual-notice predicate at a
 *           larger page (manual-renewals.queries.md D-4) rather than widening
 *           it to the broader renewal-action set, so the list modal always
 *           shows a superset consistent with what the widget body itself
 *           displays.
 *
 * Backend - VAS_259_ManualRenewalWidget/GetManualRenewalSummary     (GET -> ManualCount, NoticeOverdueCount)
 *           VAS_259_ManualRenewalWidget/GetManualRenewalContracts   (GET offset,limit -> rows + total)
 *           VAS_259_ManualRenewalWidget/GetManualRenewalContractIds (GET -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract                (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                   (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice         (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Manual Renewals — Act Before Notice   | VAS_259_Title
 *  2  | {n} manual contracts                  | VAS_259_SubCount
 *  3  | past notice date                      | VAS_259_SubOverdue
 *  4  | All                                   | VAS_259_All
 *  5  | Couldn't load                         | VAS_259_LoadError
 *  6  | Manual Renewals (list title)          | VAS_259_ListTitle
 *  7  | contracts                             | VAS_259_ContractsCount
 *  8  | Contract                              | VAS_259_ColContract
 *  9  | Value                                 | VAS_259_ColValue
 * 10  | Ends                                  | VAS_259_ColEnds
 * 11  | Notice                                | VAS_259_ColNotice
 * 12  | of                                    | VAS_259_Of
 * 13  | Previous page                         | VAS_259_PrevPage
 * 14  | Next page                             | VAS_259_NextPage
 * 15  | Nothing here right now.               | VAS_259_Empty
 * 16  | Unable to load contracts.             | VAS_259_UnableToLoad
 * 17  | Close                                 | VAS_259_Close
 * 18  | Open in browser                       | VAS_259_OpenInBrowser
 * 19  | Service Contract                      | VAS_259_ContractTitle
 * 20  | Open record                           | VAS_259_OpenRecord
 * 21  | rep                                   | VAS_259_Rep
 * 22  | cycles                                | VAS_259_Cycles
 * 23  | Contract cycle value                  | VAS_259_Value
 * 24  | Status                                | VAS_259_Status
 * 25  | Type                                  | VAS_259_Type
 * 26  | Renewal                               | VAS_259_Renewal
 * 27  | Ends                                  | VAS_259_Ends
 * 28  | in {n}d                               | VAS_259_EndsIn
 * 29  | Ended {n}d ago                        | VAS_259_EndedAgo
 * 30  | Notice days                           | VAS_259_NoticeDays
 * 31  | Billed amount                         | VAS_259_Billed
 * 32  | Unbilled amount                       | VAS_259_Unbilled
 * 33  | What needs attention                  | VAS_259_NeedsAttention
 * 34  | No open actions - contract is healthy.| VAS_259_NoOpenActions
 * 35  | Renewal notice overdue                | VAS_259_RenewalNoticeOverdue
 * 36  | Renewal notice due today              | VAS_259_RenewalNoticeDueToday
 * 37  | Manual renewal                        | VAS_259_ManualRenewal
 * 38  | notice {n}d overdue                   | VAS_259_NoticeOverdueBy
 * 39  | ends                                  | VAS_259_EndsPrefix
 * 40  | Billing overdue                       | VAS_259_BillingOverdue
 * 41  | Next period {n}d overdue              | VAS_259_NextPeriodOverdue
 * 42  | unbilled total                        | VAS_259_UnbilledTotal
 * 43  | open tickets for this customer        | VAS_259_OpenTickets
 * 44  | Billing schedule                      | VAS_259_BillingSchedule
 * 45  | Period                                | VAS_259_Period
 * 46  | Invoiced                              | VAS_259_Invoiced
 * 47  | Pending                               | VAS_259_SchedulePending
 * 48  | Generate invoice                      | VAS_259_GenerateInvoice
 * 49  | Renew                                 | VAS_259_Renew
 * 50  | Run RenewContract for this contract now? | VAS_259_ConfirmRenew
 * 51  | Generate invoices for the overdue billing periods now? | VAS_259_ConfirmGenerateInvoice
 * 52  | Working…                              | VAS_259_Working
 * 53  | The action failed.                    | VAS_259_ActionFailed
 * 54  | Couldn't load this contract.          | VAS_259_DetailLoadError
 * 55  | d (days-to-end suffix)                | VAS_259_DaysSuffix
 * 55a | d ago (past days-to-end suffix)       | VAS_259_DaysAgoSuffix
 * 56  | notice {n}d overdue                   | VAS_259_NoticeOverdue
 * 57  | notice due in {n}d                    | VAS_259_NoticeDueIn
 * 58  | ends in {n}d                          | VAS_259_EndsInLower
 * 59  | ends today                            | VAS_259_EndsToday
 * 60  | ended {n}d ago                        | VAS_259_EndedAgoLower
 * 61  | Overdue                               | VAS_259_Overdue
 * 62  | Act soon                              | VAS_259_ActSoon
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_259_ManualRenewalWidget/';

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

    // Detail-modal "Ends" stat (Sentence case, matching VAS_241/244/245/246/258).
    function endsText(days) {
        var d = Number(days || 0);
        if (d < 0) { return label('VAS_259_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_259_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    // Row meta "endText" (mock's own endText() - lower-case, distinct wording
    // from the detail-modal's Sentence-case endsText()).
    function endTextLower(days) {
        var d = Number(days || 0);
        if (d < 0) { return label('VAS_259_EndedAgoLower', 'ended {n}d ago').replace('{n}', formatCount(-d)); }
        if (d === 0) { return label('VAS_259_EndsToday', 'ends today'); }
        return label('VAS_259_EndsInLower', 'ends in {n}d').replace('{n}', formatCount(d));
    }

    // Row meta "noticeText" (manual-renewals.queries.md A.7 / prompt.md §4).
    function noticeText(noticeDeadlineDays) {
        var n = Number(noticeDeadlineDays || 0);
        if (n < 0) { return label('VAS_259_NoticeOverdue', 'notice {n}d overdue').replace('{n}', formatCount(-n)); }
        return label('VAS_259_NoticeDueIn', 'notice due in {n}d').replace('{n}', formatCount(n));
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas259-band-ok'; }
        if (code === 'EXPIRING') { return 'vas259-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas259-band-danger'; }
        return 'vas259-band-info';
    }

    function icon(name) {
        if (name === 'arrow') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>';
        }
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
        if (name === 'clock') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>';
        }
        return '';
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

    // Populates --dash-inline-size (a shared, dashboard-wide CSS var read by
    // .vas259-root's own clamp() font-size formulas) from the actual dashboard
    // grid container's width via a page-wide singleton ResizeObserver -
    // without this, clamp() falls back to 100vw and pegs near its max on any
    // normal desktop window, rendering everything larger than the mock. Same
    // helper ~180 other production widgets in this codebase already use (see
    // VAS_126_OpenTicketsWidget's own copy).
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

    VAS.VAS_259_ManualRenewalWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas259-root">');
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
                '<div class="vas259-head">' +
                    '<div class="vas259-head-l">' +
                        '<span class="vas259-iconwell">' + icon('clock') + '</span>' +
                        '<span class="vas259-head-text">' +
                            '<span class="vas259-title">' + escapeHtml(label('VAS_259_Title', 'Manual Renewals — Act Before Notice')) + '</span>' +
                            '<span class="vas259-sub vas259-skel-sub">&nbsp;</span>' +
                        '</span>' +
                    '</div>' +
                    '<button type="button" class="vas259-alllink" data-open-list>' + escapeHtml(label('VAS_259_All', 'All')) + ' ' + icon('chev') + '</button>' +
                '</div>' +
                '<div class="vas259-body">' +
                    '<div class="vas259-list"></div>' +
                    '<div class="vas259-pager"></div>' +
                '</div>'
            );
            $body = $root.find('.vas259-body');
            $bodyList = $root.find('.vas259-list');
            $bodyPager = $root.find('.vas259-pager');
            $root.on('click', '[data-open-list]', function () { openList(); });
            $root.on('click', '.vas259-row', function () { openDetail(Number($(this).attr('data-cid'))); });
            $root.on('click', '[data-row-renew]', function (e) { e.stopPropagation(); runRowRenew(Number($(this).attr('data-cid'))); });
            $root.on('click', '.vas259-pgbtn', function () { turnBodyPage($(this).attr('data-dir')); });
        }

        function subLine(count, overdueCount) {
            return formatCount(count) + ' ' + label('VAS_259_SubCount', 'manual contracts') + ' · ' +
                formatCount(overdueCount) + ' ' + label('VAS_259_SubOverdue', 'past notice date');
        }

        function renderBodyError() {
            $root.find('.vas259-sub').removeClass('vas259-skel-sub').text(label('VAS_259_LoadError', "Couldn't load"));
            $bodyList.html('<div class="vas259-state">' + escapeHtml(label('VAS_259_UnableToLoad', 'Unable to load contracts.')) + '</div>');
            $bodyPager.empty();
        }

        function loadBody() {
            var seq = ++bodySeq;
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetManualRenewalContracts',
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

        // Sub-line count is a small separate call so paging the body list never
        // re-fetches the (identical) header aggregate.
        function loadSummaryValue() {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetManualRenewalSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { return; }
                    $root.find('.vas259-sub').removeClass('vas259-skel-sub').text(subLine(parsed.ManualCount, parsed.NoticeOverdueCount));
                },
                error: function () { /* sub-line just stays as last known value */ }
            });
        }

        function rowHtml(row) {
            var overdue = !!row.NoticeOverdue;
            var meta = [noticeText(row.NoticeDeadlineDays), endTextLower(row.DaysToEnd), formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)].join(' · ');
            var chipClass = overdue ? 'vas259-chip-danger' : 'vas259-chip-warn';
            var chipText = overdue ? label('VAS_259_Overdue', 'Overdue') : label('VAS_259_ActSoon', 'Act soon');
            return '<div class="vas259-row" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas259-ic ' + (overdue ? 'vas259-ic-danger' : 'vas259-ic-warn') + '">' + icon('clock') + '</span>' +
                '<span class="vas259-row-main">' +
                    '<span class="vas259-row-name" title="' + escapeHtml(row.CustomerName || '') + '">' + escapeHtml(row.CustomerName || '') + '</span>' +
                    '<span class="vas259-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas259-chip ' + chipClass + '"><span class="vas259-dot"></span>' + escapeHtml(chipText) + '</span>' +
                '<button type="button" class="vas259-btn vas259-btn-ghost vas259-btn-sm" data-row-renew data-cid="' + Number(row.ContractId) + '">' + escapeHtml(label('VAS_259_Renew', 'Renew')) + '</button>' +
            '</div>';
        }

        function renderBody(rows) {
            if (!rows.length) {
                $bodyList.html('<div class="vas259-empty">' + icon('check') + '<span>' + escapeHtml(label('VAS_259_Empty', 'Nothing here right now.')) + '</span></div>');
                $bodyPager.empty();
                return;
            }

            $bodyList.html(rows.map(rowHtml).join(''));

            var pages = Math.max(1, Math.ceil(bodyTotal / BODY_PAGE));
            if (pages <= 1) { $bodyPager.empty(); return; }

            var current = Math.floor(bodyOffset / BODY_PAGE);
            var start = bodyOffset + 1, end = bodyOffset + rows.length;
            var of = label('VAS_259_Of', 'of');

            $bodyPager.html(
                '<span class="vas259-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(bodyTotal)) + '</span>' +
                '<span class="vas259-pgctl">' +
                    '<button type="button" class="vas259-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_259_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas259-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_259_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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

        // Row-level Renew runs standalone (confirm -> POST -> refresh the row
        // list) - it does NOT open the detail modal first. Routing it through
        // openDetail() used to show both the detail modal and the confirm
        // dialog stacked on top of each other at once (2026-09-09 fix).
        function runRowRenew(contractId) {
            if (!contractId) { return; }
            showConfirm(label('VAS_259_ConfirmRenew', 'Run RenewContract for this contract now?'), function () {
                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'RunRenew',
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            if (window.VIS && VIS.ADialog) { VIS.ADialog.error('', false, (parsed && parsed.Message) || label('VAS_259_ActionFailed', 'The action failed.'), ''); }
                            return;
                        }
                        if (window.VIS && VIS.ADialog && parsed.Message) { VIS.ADialog.info(parsed.Message); }
                        loadBody();
                    },
                    error: function () {
                        if (window.VIS && VIS.ADialog) { VIS.ADialog.error('', false, label('VAS_259_ActionFailed', 'The action failed.'), ''); }
                    }
                });
            });
        }

        /* ---------- "All" manual-renewal list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas259-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas259-scrim" data-list-close></div>' +
                    '<section class="vas259-panel">' +
                        '<header class="vas259-phead">' +
                            '<h2 class="vas259-ptitle">' + escapeHtml(label('VAS_259_ListTitle', 'Manual Renewals — Act Before Notice')) + '</h2>' +
                            '<span class="vas259-pcount"></span>' +
                            '<button type="button" class="vas259-close" data-list-close aria-label="' + escapeHtml(label('VAS_259_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas259-colhead">' +
                            '<span>' + escapeHtml(label('VAS_259_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas259-col-r">' + escapeHtml(label('VAS_259_ColValue', 'Value')) + '</span>' +
                            '<span class="vas259-col-r">' + escapeHtml(label('VAS_259_ColEnds', 'Ends')) + '</span>' +
                            '<span class="vas259-col-r">' + escapeHtml(label('VAS_259_ColNotice', 'Notice')) + '</span>' +
                        '</div>' +
                        '<div class="vas259-pbody"></div>' +
                        '<footer class="vas259-pfoot">' +
                            '<div class="vas259-pager2"></div>' +
                            '<div class="vas259-pfoot-actions">' +
                                '<button type="button" class="vas259-btn vas259-btn-ghost" data-list-close>' + escapeHtml(label('VAS_259_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas259-btn vas259-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_259_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas259-pbody');
            $listPager = $list.find('.vas259-pager2');
            $listCount = $list.find('.vas259-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas259-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas259-pgbtn', function () { turnListPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas259', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function openList() {
            listOffset = 0;
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas259-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas259-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas259-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetManualRenewalContracts',
                type: 'GET', dataType: 'json', cache: false,
                data: { offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas259-state">' + escapeHtml(label('VAS_259_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_259_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas259-state">' + escapeHtml(label('VAS_259_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            var overdue = !!row.NoticeOverdue;
            var chipClass = overdue ? 'vas259-chip-danger' : 'vas259-chip-warn';
            var chipText = overdue ? label('VAS_259_Overdue', 'Overdue') : label('VAS_259_ActSoon', 'Act soon');
            return '<div class="vas259-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas259-mtc-main">' +
                    '<span class="vas259-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas259-mtc-text">' +
                        '<span class="vas259-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas259-row-meta">' + escapeHtml([row.DocumentNo, row.ProductName].filter(function (p) { return p; }).join(' · ')) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas259-col-r"><b>' + escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</b></span>' +
                '<span class="vas259-col-r">' + escapeHtml(Number(row.DaysToEnd || 0) < 0 ? formatCount(-row.DaysToEnd) + label('VAS_259_DaysAgoSuffix', 'd ago') : formatCount(row.DaysToEnd) + label('VAS_259_DaysSuffix', 'd')) + '</span>' +
                '<span class="vas259-col-r"><span class="vas259-chip ' + chipClass + '"><span class="vas259-dot"></span>' + escapeHtml(chipText) + '</span></span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas259-state">' + escapeHtml(label('VAS_259_Empty', 'Nothing here right now.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_259_Of', 'of');

            $listPager.html(
                '<span class="vas259-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas259-pgctl">' +
                    '<button type="button" class="vas259-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_259_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas259-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_259_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
        // to exactly this widget's manual-notice population, fetched as a plain
        // Contract_ID list (GetManualRenewalContractIds) rather than
        // reconstructed as a raw where fragment client-side (see the file-header
        // Scope note for why).
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetManualRenewalContractIds',
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
                '<div class="vas259-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas259-scrim" data-detail-close></div>' +
                    '<section class="vas259-panel">' +
                        '<header class="vas259-mhead">' +
                            '<h2 class="vas259-mtitle">' + escapeHtml(label('VAS_259_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas259-mhead-meta"></span>' +
                            '<button type="button" class="vas259-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_259_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas259-mbody"></div>' +
                        '<div class="vas259-mmsg" role="status"></div>' +
                        '<footer class="vas259-mfoot">' +
                            '<button type="button" class="vas259-btn vas259-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_259_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas259-btn vas259-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_259_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas259-btn vas259-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_259_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas259-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_259_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_259_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas259-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas259-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas259-confirm-box">' +
                        '<p class="vas259-confirm-msg"></p>' +
                        '<div class="vas259-confirm-actions">' +
                            '<button type="button" class="vas259-btn vas259-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_259_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas259-btn vas259-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_259_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas259-confirm-msg');
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
            return '<div class="vas259-stat">' +
                '<span class="vas259-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas259-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
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
            $detailBody = $detail.find('.vas259-mbody');
            $detail.find('.vas259-mhead-meta').empty();
            $detailBody.html('<div class="vas259-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas259-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas259-state">' + escapeHtml(label('VAS_259_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas259-state">' + escapeHtml(label('VAS_259_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            if (row.NoticeSlackDays != null && row.NoticeSlackDays <= 0) {
                var noticeTitle = row.NoticeSlackDays < 0
                    ? label('VAS_259_RenewalNoticeOverdue', 'Renewal notice overdue')
                    : label('VAS_259_RenewalNoticeDueToday', 'Renewal notice due today');
                var noticeDescParts = [label('VAS_259_ManualRenewal', 'Manual renewal')];
                if (row.NoticeSlackDays < 0) {
                    noticeDescParts.push(label('VAS_259_NoticeOverdueBy', 'notice {n}d overdue').replace('{n}', formatCount(-row.NoticeSlackDays)));
                }
                noticeDescParts.push(label('VAS_259_EndsPrefix', 'ends') + ' ' + endsText(row.DaysToEnd));
                cards += '<div class="vas259-attn-card">' +
                    '<span class="vas259-attn-ic vas259-attn-ic-danger">' + icon('warning') + '</span>' +
                    '<span class="vas259-attn-main">' +
                        '<span class="vas259-attn-title2">' + escapeHtml(noticeTitle) + '</span>' +
                        '<span class="vas259-attn-desc">' + escapeHtml(noticeDescParts.join(' · ')) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_259_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_259_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas259-attn-card">' +
                    '<span class="vas259-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas259-attn-main">' +
                        '<span class="vas259-attn-title2">' + escapeHtml(label('VAS_259_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas259-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas259-attn-empty">' + escapeHtml(label('VAS_259_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas259-attn">' +
                '<div class="vas259-attn-title">' + escapeHtml(label('VAS_259_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_259_Period', 'Period');
            var invoicedLabel = label('VAS_259_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_259_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas259-band-ok' : 'vas259-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas259-sched-row">' +
                    '<span class="vas259-sched-left">' + left + '</span>' +
                    '<span class="vas259-sched-right">' +
                        '<span class="vas259-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas259-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas259-sched">' +
                '<div class="vas259-sched-title">' + escapeHtml(label('VAS_259_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas259-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas259-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_259_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_259_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas259-dtop2">' +
                    '<span class="vas259-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas259-dhead-main">' +
                        '<span class="vas259-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas259-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas259-dhead-right">' +
                        '<span class="vas259-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas259-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas259-stats">' +
                    statHtml('VAS_259_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_259_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_259_Type', 'Type', row.ContractType) +
                    statHtml('VAS_259_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_259_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_259_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_259_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_259_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
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
                showDetailMessage(label('VAS_259_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_259_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadBody();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_259_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas259-modal-open'); }
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
            $(document).off('keydown.MPCvas259');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas259-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_259_ManualRenewalWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_259_ManualRenewalWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_259_ManualRenewalWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_259_ManualRenewalWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_259_ManualRenewalWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_259_ManualRenewalWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
