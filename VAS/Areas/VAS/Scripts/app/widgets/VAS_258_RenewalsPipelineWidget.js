/**
 * VAS_258 Renewals Pipeline Widget (Service Contracts dashboard)
 * Purpose - c5 x r3 CENTERPIECE list widget: live service contracts ending
 *           within 6 months (daysToEnd <= 180), soonest-ending first, so the
 *           Contracts Manager can work the renewal queue top-down. Header
 *           sub-line = "N live contracts ending <=6 months - $X" (count +
 *           base-currency value); the header "All ->" link opens the full
 *           pipeline list modal (paged @7, same predicate, larger visible
 *           columns). Each row shows a leading end-countdown tile, the
 *           customer (title), DocumentNo / product / notice-or-auto text
 *           (meta), a decoded renewal-type chip (Auto/Manual - neutral
 *           surface, coloured dot only), the base-currency contract value and
 *           a Renew button. Row -> the shared contract detail modal (reusing
 *           VAS_241_ContractSearchWidget's GetContract / RunRenew /
 *           RunGenerateInvoice endpoints, the same cross-widget endpoint-reuse
 *           pattern VAS_120 set with VAS_126 and VAS_244/VAS_245/VAS_246
 *           already reused too), whose "Renew" runs the real RenewContract
 *           process and "Open record" zooms to the Service Contract window.
 *           The widget never resizes on paging - fixed c5 x r3 footprint,
 *           outer overflow hidden, a full page shows all 7 rows with no
 *           clipping.
 * Design  - renewals-pipeline.html (the parent Service Contracts dashboard
 *           mock, single-widget preview of renewalPipelineWidget()) is the
 *           pixel-level source of truth; re-created verbatim below (tokens,
 *           cdays end-countdown tile, neutral renewal chip with coloured dot,
 *           header icon well/title/sub/"All ->" link, row structure, pager).
 * Scope   - "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly this widget's pipeline
 *           population, fetched as a plain Contract_ID list
 *           (GetRenewalsPipelineContractIds) rather than reconstructed as a
 *           raw DAYSBETWEEN/CURRENT_DATE where fragment client-side (this
 *           codebase's own VAS_140 widget shows that inlining relative date
 *           arithmetic into a raw where-clause breaks across Postgres/
 *           Oracle). When hosted (windowNo >= 0) this goes through
 *           widgetFirevalueChanged / ActionName - the same channel "Open
 *           record" already uses - resolved by NAME through the host window
 *           framework (VAS_244/245/246's own openInBrowser fix, 2026-09-08:
 *           VAS.ZoomUtil's hardcoded AD_Window_ID 1000248 turned out to
 *           resolve to a different window (Lead) on the real install).
 *
 * Backend - VAS_258_RenewalsPipelineWidget/GetRenewalsPipelineSummary    (GET -> PipelineCount, PipelineValueBase, currency)
 *           VAS_258_RenewalsPipelineWidget/GetRenewalsPipelineContracts  (GET offset,limit -> rows + total)
 *           VAS_258_RenewalsPipelineWidget/GetRenewalsPipelineContractIds (GET -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract                    (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                       (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice             (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Renewals Pipeline                     | VAS_258_Title
 *  2  | {n} live contracts ending <=6 months  | VAS_258_SubLine
 *  3  | All                                   | VAS_258_All
 *  4  | Couldn't load                         | VAS_258_LoadError
 *  5  | Renewals Pipeline (list title)        | VAS_258_ListTitle
 *  6  | contracts                             | VAS_258_ContractsCount
 *  7  | Contract                              | VAS_258_ColContract
 *  8  | Value                                 | VAS_258_ColValue
 *  9  | Ends                                  | VAS_258_ColEnds
 * 10  | Renewal                               | VAS_258_ColRenewal
 * 11  | Status                                | VAS_258_ColStatus
 * 12  | of                                    | VAS_258_Of
 * 13  | Previous page                         | VAS_258_PrevPage
 * 14  | Next page                             | VAS_258_NextPage
 * 15  | Nothing here right now.               | VAS_258_Empty
 * 16  | Unable to load contracts.             | VAS_258_UnableToLoad
 * 17  | Close                                 | VAS_258_Close
 * 18  | Open in browser                       | VAS_258_OpenInBrowser
 * 19  | Service Contract                      | VAS_258_ContractTitle
 * 20  | Open record                           | VAS_258_OpenRecord
 * 21  | rep                                   | VAS_258_Rep
 * 22  | cycles                                | VAS_258_Cycles
 * 23  | Contract cycle value                  | VAS_258_Value
 * 24  | Status                                | VAS_258_Status
 * 25  | Type                                  | VAS_258_Type
 * 26  | Renewal                               | VAS_258_Renewal
 * 27  | Ends                                  | VAS_258_Ends
 * 28  | in {n}d                               | VAS_258_EndsIn
 * 29  | Ended {n}d ago                        | VAS_258_EndedAgo
 * 30  | Notice days                           | VAS_258_NoticeDays
 * 31  | Billed amount                         | VAS_258_Billed
 * 32  | Unbilled amount                       | VAS_258_Unbilled
 * 33  | What needs attention                  | VAS_258_NeedsAttention
 * 34  | No open actions - contract is healthy.| VAS_258_NoOpenActions
 * 35  | Renewal notice overdue                | VAS_258_RenewalNoticeOverdue
 * 36  | Renewal notice due today              | VAS_258_RenewalNoticeDueToday
 * 37  | Manual renewal                        | VAS_258_ManualRenewal
 * 38  | notice {n}d overdue                   | VAS_258_NoticeOverdueBy
 * 39  | ends                                  | VAS_258_EndsPrefix
 * 40  | Billing overdue                       | VAS_258_BillingOverdue
 * 41  | Next period {n}d overdue              | VAS_258_NextPeriodOverdue
 * 42  | unbilled total                        | VAS_258_UnbilledTotal
 * 43  | open tickets for this customer        | VAS_258_OpenTickets
 * 44  | Billing schedule                      | VAS_258_BillingSchedule
 * 45  | Period                                | VAS_258_Period
 * 46  | Invoiced                              | VAS_258_Invoiced
 * 47  | Pending                               | VAS_258_SchedulePending
 * 48  | Generate invoice                      | VAS_258_GenerateInvoice
 * 49  | Renew                                 | VAS_258_Renew
 * 50  | Run RenewContract for this contract now? | VAS_258_ConfirmRenew
 * 51  | Generate invoices for the overdue billing periods now? | VAS_258_ConfirmGenerateInvoice
 * 52  | Working…                              | VAS_258_Working
 * 53  | The action failed.                    | VAS_258_ActionFailed
 * 54  | Couldn't load this contract.          | VAS_258_DetailLoadError
 * 55  | d (days-to-end suffix)                | VAS_258_DaysSuffix
 * 55a | d ago (past days-to-end suffix)       | VAS_258_DaysAgoSuffix
 * 56  | auto-renews                           | VAS_258_AutoRenews
 * 57  | notice due in {n}d                    | VAS_258_NoticeDueIn
 * 58  | notice {n}d overdue                    | VAS_258_NoticeOverdue
 * 59  | ended                                  | VAS_258_Ended
 * 60  | to end                                 | VAS_258_ToEnd
 * 61  | value                                  | VAS_258_ValueCaption
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_258_RenewalsPipelineWidget/';

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
        if (d < 0) { return label('VAS_258_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_258_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    function isManualRenewal(code) {
        var c = String(code || '').toUpperCase();
        return c === 'M' || c === 'MNL';
    }

    // Row meta text (renewals-pipeline.prompt.md §4): manual contracts show the
    // notice deadline slack (overdue / due-in), auto contracts show "auto-renews".
    function noticeOrAutoText(row) {
        if (!isManualRenewal(row.RenewalTypeCode)) { return label('VAS_258_AutoRenews', 'auto-renews'); }
        var n = Number(row.NoticeDeadlineDays || 0);
        if (n < 0) { return label('VAS_258_NoticeOverdue', 'notice {n}d overdue').replace('{n}', formatCount(-n)); }
        return label('VAS_258_NoticeDueIn', 'notice due in {n}d').replace('{n}', formatCount(n));
    }

    // Rows can be Auto or Manual - the badge colour is resolved per row.
    function renewalClass(code) {
        var c = String(code || '').toUpperCase();
        if (c === 'A' || c === 'ATC') { return 'vas258-renewal-auto'; }
        if (c === 'M' || c === 'MNL') { return 'vas258-renewal-manual'; }
        return 'vas258-renewal-neutral';
    }

    // Leading end-countdown tile (renewals-pipeline.prompt.md §7): green >90d /
    // amber <=90d / red past-end.
    function cdaysClass(days) {
        var d = Number(days || 0);
        if (d < 0) { return 'vas258-cd-lo'; }
        if (d <= 90) { return 'vas258-cd-warn'; }
        return 'vas258-cd-ok';
    }
    function cdaysHtml(days) {
        var d = Number(days || 0);
        var big = d < 0 ? '—' : formatCount(d) + escapeHtml(label('VAS_258_DaysSuffix', 'd'));
        var small = d < 0 ? label('VAS_258_Ended', 'ended') : label('VAS_258_ToEnd', 'to end');
        return '<span class="vas258-cdays ' + cdaysClass(d) + '"><b>' + big + '</b><span>' + escapeHtml(small) + '</span></span>';
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas258-band-ok'; }
        if (code === 'EXPIRING') { return 'vas258-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas258-band-danger'; }
        return 'vas258-band-info';
    }

    // Every pipeline row is already known live and <=180 days to end - the list
    // modal's own Status column reads the same <=90-day threshold VAS_244/245
    // already use, mirroring the mock's own daysClass()/bandTag() split.
    function rowLifecycleStatus(daysToEnd) {
        var d = Number(daysToEnd || 0);
        if (d < 0) { return label('VAS_258_StatusEnded', 'Ended'); }
        if (d <= 90) { return label('VAS_258_StatusExpiring', 'Expiring'); }
        return label('VAS_258_StatusActive', 'Active');
    }
    function rowLifecycleClass(daysToEnd) {
        var d = Number(daysToEnd || 0);
        if (d < 0) { return 'vas258-band-danger'; }
        if (d <= 90) { return 'vas258-band-warn'; }
        return 'vas258-band-ok';
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
        return '';
    }

    // Populates --dash-inline-size (a shared, dashboard-wide CSS var read by
    // .vas258-root's own clamp() font-size formulas) from the actual dashboard
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

    VAS.VAS_258_RenewalsPipelineWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas258-root">');
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
                '<div class="vas258-head">' +
                    '<div class="vas258-head-l">' +
                        '<span class="vas258-iconwell">' + icon('refresh') + '</span>' +
                        '<span class="vas258-head-text">' +
                            '<span class="vas258-title">' + escapeHtml(label('VAS_258_Title', 'Renewals Pipeline')) + '</span>' +
                            '<span class="vas258-sub vas258-skel-sub">&nbsp;</span>' +
                        '</span>' +
                    '</div>' +
                    '<button type="button" class="vas258-alllink" data-open-list>' + escapeHtml(label('VAS_258_All', 'All')) + ' ' + icon('chev') + '</button>' +
                '</div>' +
                '<div class="vas258-body">' +
                    '<div class="vas258-list"></div>' +
                    '<div class="vas258-pager"></div>' +
                '</div>'
            );
            $body = $root.find('.vas258-body');
            $bodyList = $root.find('.vas258-list');
            $bodyPager = $root.find('.vas258-pager');
            $root.on('click', '[data-open-list]', function () { openList(); });
            $root.on('click', '.vas258-row', function () { openDetail(Number($(this).attr('data-cid'))); });
            $root.on('click', '[data-row-renew]', function (e) { e.stopPropagation(); runRowRenew(Number($(this).attr('data-cid'))); });
            $root.on('click', '.vas258-pgbtn', function () { turnBodyPage($(this).attr('data-dir')); });
        }

        function subLine(count, valueText) {
            return formatCount(count) + ' ' + label('VAS_258_SubLine', 'live contracts ending ≤6 months') + ' · ' + valueText;
        }

        function renderBodyError() {
            $root.find('.vas258-sub').removeClass('vas258-skel-sub').text(label('VAS_258_LoadError', "Couldn't load"));
            $bodyList.html('<div class="vas258-state">' + escapeHtml(label('VAS_258_UnableToLoad', 'Unable to load contracts.')) + '</div>');
            $bodyPager.empty();
        }

        function loadBody() {
            var seq = ++bodySeq;
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRenewalsPipelineContracts',
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
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRenewalsPipelineSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { return; }
                    var valueText = formatMoney(parsed.PipelineValueBase, parsed.CurrencyIso, parsed.CurrencySymbol, parsed.CurrencyPrecision);
                    $root.find('.vas258-sub').removeClass('vas258-skel-sub').text(subLine(parsed.PipelineCount, valueText));
                },
                error: function () { /* sub-line just stays as last known value */ }
            });
        }

        function rowHtml(row) {
            var meta = [row.DocumentNo, row.ProductName, noticeOrAutoText(row)].filter(function (p) { return p; }).join(' · ');
            return '<div class="vas258-row" data-cid="' + Number(row.ContractId) + '">' +
                cdaysHtml(row.DaysToEnd) +
                '<span class="vas258-row-main">' +
                    '<span class="vas258-row-name" title="' + escapeHtml(row.CustomerName || '') + '">' + escapeHtml(row.CustomerName || '') + '</span>' +
                    '<span class="vas258-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas258-renewal ' + renewalClass(row.RenewalTypeCode) + '"><span class="vas258-dot"></span>' + escapeHtml(row.RenewalType || '') + '</span>' +
                '<span class="vas258-row-val">' +
                    '<span class="vas258-row-amt">' + escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                    '<span class="vas258-row-vcap">' + escapeHtml(label('VAS_258_ValueCaption', 'value')) + '</span>' +
                '</span>' +
                '<button type="button" class="vas258-btn vas258-btn-primary vas258-btn-sm" data-row-renew data-cid="' + Number(row.ContractId) + '">' + escapeHtml(label('VAS_258_Renew', 'Renew')) + '</button>' +
            '</div>';
        }

        function renderBody(rows) {
            if (!rows.length) {
                $bodyList.html('<div class="vas258-empty">' + icon('check') + '<span>' + escapeHtml(label('VAS_258_Empty', 'Nothing here right now.')) + '</span></div>');
                $bodyPager.empty();
                return;
            }

            $bodyList.html(rows.map(rowHtml).join(''));

            var pages = Math.max(1, Math.ceil(bodyTotal / BODY_PAGE));
            if (pages <= 1) { $bodyPager.empty(); return; }

            var current = Math.floor(bodyOffset / BODY_PAGE);
            var start = bodyOffset + 1, end = bodyOffset + rows.length;
            var of = label('VAS_258_Of', 'of');

            $bodyPager.html(
                '<span class="vas258-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(bodyTotal)) + '</span>' +
                '<span class="vas258-pgctl">' +
                    '<button type="button" class="vas258-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_258_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas258-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_258_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
            showConfirm(label('VAS_258_ConfirmRenew', 'Run RenewContract for this contract now?'), function () {
                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'RunRenew',
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            if (window.VIS && VIS.ADialog) { VIS.ADialog.error('', false, (parsed && parsed.Message) || label('VAS_258_ActionFailed', 'The action failed.'), ''); }
                            return;
                        }
                        if (window.VIS && VIS.ADialog && parsed.Message) { VIS.ADialog.info(parsed.Message); }
                        loadBody();
                    },
                    error: function () {
                        if (window.VIS && VIS.ADialog) { VIS.ADialog.error('', false, label('VAS_258_ActionFailed', 'The action failed.'), ''); }
                    }
                });
            });
        }

        /* ---------- "All" pipeline list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas258-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas258-scrim" data-list-close></div>' +
                    '<section class="vas258-panel">' +
                        '<header class="vas258-phead">' +
                            '<h2 class="vas258-ptitle">' + escapeHtml(label('VAS_258_ListTitle', 'Renewals Pipeline')) + '</h2>' +
                            '<span class="vas258-pcount"></span>' +
                            '<button type="button" class="vas258-close" data-list-close aria-label="' + escapeHtml(label('VAS_258_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas258-colhead">' +
                            '<span>' + escapeHtml(label('VAS_258_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas258-col-r">' + escapeHtml(label('VAS_258_ColValue', 'Value')) + '</span>' +
                            '<span class="vas258-col-r">' + escapeHtml(label('VAS_258_ColEnds', 'Ends')) + '</span>' +
                            '<span>' + escapeHtml(label('VAS_258_ColRenewal', 'Renewal')) + '</span>' +
                            '<span class="vas258-col-r">' + escapeHtml(label('VAS_258_ColStatus', 'Status')) + '</span>' +
                        '</div>' +
                        '<div class="vas258-pbody"></div>' +
                        '<footer class="vas258-pfoot">' +
                            '<div class="vas258-pager2"></div>' +
                            '<div class="vas258-pfoot-actions">' +
                                '<button type="button" class="vas258-btn vas258-btn-ghost" data-list-close>' + escapeHtml(label('VAS_258_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas258-btn vas258-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_258_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas258-pbody');
            $listPager = $list.find('.vas258-pager2');
            $listCount = $list.find('.vas258-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas258-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas258-pgbtn', function () { turnListPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas258', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function openList() {
            listOffset = 0;
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas258-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas258-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas258-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRenewalsPipelineContracts',
                type: 'GET', dataType: 'json', cache: false,
                data: { offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas258-state">' + escapeHtml(label('VAS_258_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_258_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas258-state">' + escapeHtml(label('VAS_258_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            return '<div class="vas258-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas258-mtc-main">' +
                    '<span class="vas258-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas258-mtc-text">' +
                        '<span class="vas258-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas258-row-meta">' + escapeHtml([row.DocumentNo, row.ProductName].filter(function (p) { return p; }).join(' · ')) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas258-col-r"><b>' + escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</b></span>' +
                '<span class="vas258-col-r">' + escapeHtml(Number(row.DaysToEnd || 0) < 0 ? formatCount(-row.DaysToEnd) + label('VAS_258_DaysAgoSuffix', 'd ago') : formatCount(row.DaysToEnd) + label('VAS_258_DaysSuffix', 'd')) + '</span>' +
                '<span><span class="vas258-renewal ' + renewalClass(row.RenewalTypeCode) + '"><span class="vas258-dot"></span>' + escapeHtml(row.RenewalType || '') + '</span></span>' +
                '<span class="vas258-col-r"><span class="vas258-status ' + rowLifecycleClass(row.DaysToEnd) + '">' + escapeHtml(rowLifecycleStatus(row.DaysToEnd)) + '</span></span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas258-state">' + escapeHtml(label('VAS_258_Empty', 'Nothing here right now.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_258_Of', 'of');

            $listPager.html(
                '<span class="vas258-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas258-pgctl">' +
                    '<button type="button" class="vas258-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_258_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas258-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_258_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
        // to exactly this widget's pipeline population, fetched as a plain
        // Contract_ID list (GetRenewalsPipelineContractIds) rather than
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRenewalsPipelineContractIds',
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
                '<div class="vas258-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas258-scrim" data-detail-close></div>' +
                    '<section class="vas258-panel">' +
                        '<header class="vas258-mhead">' +
                            '<h2 class="vas258-mtitle">' + escapeHtml(label('VAS_258_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas258-mhead-meta"></span>' +
                            '<button type="button" class="vas258-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_258_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas258-mbody"></div>' +
                        '<div class="vas258-mmsg" role="status"></div>' +
                        '<footer class="vas258-mfoot">' +
                            '<button type="button" class="vas258-btn vas258-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_258_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas258-btn vas258-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_258_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas258-btn vas258-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_258_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas258-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_258_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_258_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas258-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas258-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas258-confirm-box">' +
                        '<p class="vas258-confirm-msg"></p>' +
                        '<div class="vas258-confirm-actions">' +
                            '<button type="button" class="vas258-btn vas258-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_258_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas258-btn vas258-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_258_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas258-confirm-msg');
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
            return '<div class="vas258-stat">' +
                '<span class="vas258-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas258-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
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
            $detailBody = $detail.find('.vas258-mbody');
            $detail.find('.vas258-mhead-meta').empty();
            $detailBody.html('<div class="vas258-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas258-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas258-state">' + escapeHtml(label('VAS_258_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas258-state">' + escapeHtml(label('VAS_258_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            if (isManualRenewal(row.RenewalTypeCode) && !row.HasSuccessor && row.NoticeSlackDays != null && row.NoticeSlackDays <= 0) {
                var noticeTitle = row.NoticeSlackDays < 0
                    ? label('VAS_258_RenewalNoticeOverdue', 'Renewal notice overdue')
                    : label('VAS_258_RenewalNoticeDueToday', 'Renewal notice due today');
                var noticeDescParts = [label('VAS_258_ManualRenewal', 'Manual renewal')];
                if (row.NoticeSlackDays < 0) {
                    noticeDescParts.push(label('VAS_258_NoticeOverdueBy', 'notice {n}d overdue').replace('{n}', formatCount(-row.NoticeSlackDays)));
                }
                noticeDescParts.push(label('VAS_258_EndsPrefix', 'ends') + ' ' + endsText(row.DaysToEnd));
                cards += '<div class="vas258-attn-card">' +
                    '<span class="vas258-attn-ic vas258-attn-ic-danger">' + icon('warning') + '</span>' +
                    '<span class="vas258-attn-main">' +
                        '<span class="vas258-attn-title2">' + escapeHtml(noticeTitle) + '</span>' +
                        '<span class="vas258-attn-desc">' + escapeHtml(noticeDescParts.join(' · ')) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_258_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_258_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas258-attn-card">' +
                    '<span class="vas258-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas258-attn-main">' +
                        '<span class="vas258-attn-title2">' + escapeHtml(label('VAS_258_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas258-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas258-attn-empty">' + escapeHtml(label('VAS_258_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas258-attn">' +
                '<div class="vas258-attn-title">' + escapeHtml(label('VAS_258_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_258_Period', 'Period');
            var invoicedLabel = label('VAS_258_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_258_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas258-band-ok' : 'vas258-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas258-sched-row">' +
                    '<span class="vas258-sched-left">' + left + '</span>' +
                    '<span class="vas258-sched-right">' +
                        '<span class="vas258-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas258-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas258-sched">' +
                '<div class="vas258-sched-title">' + escapeHtml(label('VAS_258_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas258-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas258-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_258_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_258_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas258-dtop2">' +
                    '<span class="vas258-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas258-dhead-main">' +
                        '<span class="vas258-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas258-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas258-dhead-right">' +
                        '<span class="vas258-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas258-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas258-stats">' +
                    statHtml('VAS_258_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_258_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_258_Type', 'Type', row.ContractType) +
                    statHtml('VAS_258_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_258_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_258_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_258_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_258_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
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
                showDetailMessage(label('VAS_258_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_258_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadBody();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_258_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas258-modal-open'); }
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
            $(document).off('keydown.MPCvas258');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas258-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_258_RenewalsPipelineWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_258_RenewalsPipelineWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_258_RenewalsPipelineWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_258_RenewalsPipelineWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_258_RenewalsPipelineWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_258_RenewalsPipelineWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
