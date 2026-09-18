/**
 * VAS_265 Service Under Contract Widget (Service Contracts dashboard)
 * Purpose - c3 x r2 triage-list widget: live contracts (Processed='Y',
 *           IsCancel='N', EndDate >= CURRENT_DATE) that carry at least one
 *           open ticket, most-loaded (open-ticket count DESC, EndDate ASC
 *           tiebreak) first - lets the contracts manager see which active
 *           agreements are generating service load right now. Header
 *           sub-line = "N tickets · M assets · R_Request/A_Asset"; the header
 *           "All ->" link opens the full list modal. Each row shows a leading
 *           ticket-icon tile (info tint, warn when tickets >= 4), the
 *           customer (title), a coverage meta-line ("N open tickets · M
 *           assets") and a right-aligned neutral count chip (info/warn dot).
 *           Read-only triage list - no per-row action button. Row -> the
 *           shared contract detail modal (reusing
 *           VAS_241_ContractSearchWidget's GetContract / RunRenew /
 *           RunGenerateInvoice endpoints, the same cross-widget endpoint-reuse
 *           pattern VAS_120 set with VAS_126 and VAS_244/245/246/258/259/264
 *           already reused too), whose "Renew"/"Generate invoice" run the
 *           real processes and "Open record" zooms to the Service Contract
 *           window. The widget never resizes on paging - fixed c3 x r2
 *           footprint, outer overflow hidden, a full page shows all 4 rows
 *           with no clipping.
 * Design  - service-under-contract.html (the parent Service Contracts
 *           dashboard mock, single-widget preview of coverageWidget()) is the
 *           pixel-level source of truth; re-created verbatim below (tokens,
 *           info/warn-tinted leading ticket-icon tile, neutral count chip,
 *           header icon well/title/sub/"All ->" link, row structure, pager).
 *           The "All" list modal is a 3-column table (Contract / Tickets /
 *           Assets) since this widget's row DTO carries no Value/Renewal/
 *           Status columns (service-under-contract.prompt.md §3 - the row
 *           list shows coverage counts, not money or lifecycle state).
 * Scope   - The open-ticket count is NOT correlated via R_Request.
 *           C_Contract_ID as the build spec assumed: VAS_241_
 *           ContractSearchWidget already established, against the real
 *           schema, that R_Request carries no C_Contract_ID column at all.
 *           Per explicit direction this widget's backend instead correlates
 *           a ticket to a contract through BOTH R_Request.C_BPartner_ID and
 *           R_Request.M_Product_ID matching the contract's own columns - see
 *           the controller's file-header note for the full rationale and its
 *           known limits. The VA075_WorkOrder optional coverage count (build
 *           spec section 3, flagged unverified) is omitted entirely - that
 *           module is not part of this solution/schema at all.
 *           "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly this widget's
 *           population, fetched as a plain Contract_ID list
 *           (GetServiceUnderContractContractIds) rather than reconstructed as
 *           a raw correlated-EXISTS where fragment client-side (this
 *           codebase's own VAS_140 widget shows that inlining relative
 *           predicates into a raw where-clause breaks across Postgres/
 *           Oracle). When hosted (windowNo >= 0) this goes through
 *           widgetFirevalueChanged / ActionName - the same channel "Open
 *           record" already uses - resolved by NAME through the host window
 *           framework (VAS_244/245/246/258/259/264's own openInBrowser fix,
 *           2026-09-08: VAS.ZoomUtil's hardcoded AD_Window_ID 1000248 turned
 *           out to resolve to a different window (Lead) on the real install).
 *
 * Backend - VAS_265_ServiceUnderContractWidget/GetServiceUnderContractSummary     (GET -> OpenTicketTotal, AssetTotal)
 *           VAS_265_ServiceUnderContractWidget/GetServiceUnderContractContracts   (GET offset,limit -> rows + total)
 *           VAS_265_ServiceUnderContractWidget/GetServiceUnderContractContractIds (GET -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract                             (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                                (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice                      (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Service Under Contract                | VAS_265_Title
 *  2  | {n} tickets                           | VAS_265_SubTickets
 *  3  | {n} assets                            | VAS_265_SubAssets
 *  4  | All                                   | VAS_265_All
 *  5  | Couldn't load                         | VAS_265_LoadError
 *  6  | Service Under Contract (list title)   | VAS_265_ListTitle
 *  7  | contracts                             | VAS_265_ContractsCount
 *  8  | Contract                              | VAS_265_ColContract
 *  9  | Tickets                               | VAS_265_ColTickets
 * 10  | Assets                                | VAS_265_ColAssets
 * 11  | of                                    | VAS_265_Of
 * 12  | Previous page                         | VAS_265_PrevPage
 * 13  | Next page                             | VAS_265_NextPage
 * 14  | Nothing here right now.               | VAS_265_Empty
 * 15  | Unable to load contracts.             | VAS_265_UnableToLoad
 * 16  | Close                                 | VAS_265_Close
 * 17  | Open in browser                       | VAS_265_OpenInBrowser
 * 18  | Service Contract                      | VAS_265_ContractTitle
 * 19  | Open record                           | VAS_265_OpenRecord
 * 20  | rep                                   | VAS_265_Rep
 * 21  | cycles                                | VAS_265_Cycles
 * 22  | Contract cycle value                  | VAS_265_Value
 * 23  | Status                                | VAS_265_Status
 * 24  | Type                                  | VAS_265_Type
 * 25  | Renewal                               | VAS_265_Renewal
 * 26  | Ends                                  | VAS_265_Ends
 * 27  | in {n}d                               | VAS_265_EndsIn
 * 28  | Ended {n}d ago                        | VAS_265_EndedAgo
 * 29  | Notice days                           | VAS_265_NoticeDays
 * 30  | Billed amount                         | VAS_265_Billed
 * 31  | Unbilled amount                       | VAS_265_Unbilled
 * 32  | What needs attention                  | VAS_265_NeedsAttention
 * 33  | No open actions - contract is healthy.| VAS_265_NoOpenActions
 * 34  | open tickets under contract           | VAS_265_OpenTicketsUnderContract
 * 35  | Billing overdue                       | VAS_265_BillingOverdue
 * 36  | Next period {n}d overdue              | VAS_265_NextPeriodOverdue
 * 37  | unbilled total                        | VAS_265_UnbilledTotal
 * 38  | Billing schedule                      | VAS_265_BillingSchedule
 * 39  | Period                                | VAS_265_Period
 * 40  | Invoiced                              | VAS_265_Invoiced
 * 41  | Pending                               | VAS_265_SchedulePending
 * 42  | Generate invoice                      | VAS_265_GenerateInvoice
 * 43  | Renew                                 | VAS_265_Renew
 * 44  | Run RenewContract for this contract now? | VAS_265_ConfirmRenew
 * 45  | Generate invoices for the overdue billing periods now? | VAS_265_ConfirmGenerateInvoice
 * 46  | Working…                              | VAS_265_Working
 * 47  | The action failed.                    | VAS_265_ActionFailed
 * 48  | Couldn't load this contract.          | VAS_265_DetailLoadError
 * 49  | open ticket                           | VAS_265_OpenTicketSingular
 * 50  | open tickets                          | VAS_265_OpenTicketPlural
 * 51  | assets                                | VAS_265_AssetsSuffix
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_265_ServiceUnderContractWidget/';

    var BODY_PAGE = 4;
    var LIST_PAGE = 4;
    var WARN_THRESHOLD = 4;

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

    // Detail-modal "Ends" stat (Sentence case, matching VAS_241/244/245/246/258/259/264).
    function endsText(days) {
        var d = Number(days || 0);
        if (d < 0) { return label('VAS_265_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_265_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    // Row meta coverage line (service-under-contract.prompt.md §1/§7):
    // "N open ticket(s) · M assets".
    function coverageLine(ticketCount, assetCount) {
        var n = Number(ticketCount || 0);
        var ticketsText = formatCount(n) + ' ' + (n === 1
            ? label('VAS_265_OpenTicketSingular', 'open ticket')
            : label('VAS_265_OpenTicketPlural', 'open tickets'));
        var parts = [ticketsText];
        if (Number(assetCount || 0) > 0) {
            parts.push(formatCount(assetCount) + ' ' + label('VAS_265_AssetsSuffix', 'assets'));
        }
        return parts.join(' · ');
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas265-band-ok'; }
        if (code === 'EXPIRING') { return 'vas265-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas265-band-danger'; }
        return 'vas265-band-info';
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
        if (name === 'check') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';
        }
        if (name === 'ticket') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 9a3 3 0 0 1 0 6v2a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-2a3 3 0 0 1 0-6V7a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2z"></path><path d="M13 5v2M13 11v2M13 17v2"></path></svg>';
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
    // .vas265-root's own clamp() font-size formulas) from the actual dashboard
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

    VAS.VAS_265_ServiceUnderContractWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas265-root">');
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
                '<div class="vas265-head">' +
                    '<div class="vas265-head-l">' +
                        '<span class="vas265-iconwell">' + icon('ticket') + '</span>' +
                        '<span class="vas265-head-text">' +
                            '<span class="vas265-title">' + escapeHtml(label('VAS_265_Title', 'Service Under Contract')) + '</span>' +
                            '<span class="vas265-sub vas265-skel-sub">&nbsp;</span>' +
                        '</span>' +
                    '</div>' +
                    '<button type="button" class="vas265-alllink" data-open-list>' + escapeHtml(label('VAS_265_All', 'All')) + ' ' + icon('chev') + '</button>' +
                '</div>' +
                '<div class="vas265-body">' +
                    '<div class="vas265-list"></div>' +
                    '<div class="vas265-pager"></div>' +
                '</div>'
            );
            $body = $root.find('.vas265-body');
            $bodyList = $root.find('.vas265-list');
            $bodyPager = $root.find('.vas265-pager');
            $root.on('click', '[data-open-list]', function () { openList(); });
            $root.on('click', '.vas265-row', function () { openDetail(Number($(this).attr('data-cid'))); });
            $root.on('click', '.vas265-pgbtn', function () { turnBodyPage($(this).attr('data-dir')); });
        }

        function subLine(ticketTotal, assetTotal) {
            return formatCount(ticketTotal) + ' ' + label('VAS_265_SubTickets', 'tickets') + ' · ' +
                formatCount(assetTotal) + ' ' + label('VAS_265_SubAssets', 'assets') + ' · R_Request/A_Asset';
        }

        function renderBodyError() {
            $root.find('.vas265-sub').removeClass('vas265-skel-sub').text(label('VAS_265_LoadError', "Couldn't load"));
            $bodyList.html('<div class="vas265-state">' + escapeHtml(label('VAS_265_UnableToLoad', 'Unable to load contracts.')) + '</div>');
            $bodyPager.empty();
        }

        function loadBody() {
            var seq = ++bodySeq;
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetServiceUnderContractContracts',
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

        // Sub-line totals are a small separate call so paging the body list
        // never re-fetches the (identical) header aggregate.
        function loadSummaryValue() {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetServiceUnderContractSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { return; }
                    $root.find('.vas265-sub').removeClass('vas265-skel-sub').text(subLine(parsed.OpenTicketTotal, parsed.AssetTotal));
                },
                error: function () { /* sub-line just stays as last known value */ }
            });
        }

        function rowHtml(row) {
            var warn = Number(row.OpenTicketCount || 0) >= WARN_THRESHOLD;
            var meta = coverageLine(row.OpenTicketCount, row.AssetCount);
            return '<div class="vas265-row" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas265-ic ' + (warn ? 'vas265-ic-warn' : 'vas265-ic-info') + '">' + icon('ticket') + '</span>' +
                '<span class="vas265-row-main">' +
                    '<span class="vas265-row-name" title="' + escapeHtml(row.CustomerName || '') + '">' + escapeHtml(row.CustomerName || '') + '</span>' +
                    '<span class="vas265-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas265-chip ' + (warn ? 'vas265-chip-warn' : 'vas265-chip-info') + '"><span class="vas265-dot"></span>' + formatCount(row.OpenTicketCount) + '</span>' +
            '</div>';
        }

        function renderBody(rows) {
            if (!rows.length) {
                $bodyList.html('<div class="vas265-empty">' + icon('check') + '<span>' + escapeHtml(label('VAS_265_Empty', 'Nothing here right now.')) + '</span></div>');
                $bodyPager.empty();
                return;
            }

            $bodyList.html(rows.map(rowHtml).join(''));

            var pages = Math.max(1, Math.ceil(bodyTotal / BODY_PAGE));
            if (pages <= 1) { $bodyPager.empty(); return; }

            var current = Math.floor(bodyOffset / BODY_PAGE);
            var start = bodyOffset + 1, end = bodyOffset + rows.length;
            var of = label('VAS_265_Of', 'of');

            $bodyPager.html(
                '<span class="vas265-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(bodyTotal)) + '</span>' +
                '<span class="vas265-pgctl">' +
                    '<button type="button" class="vas265-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_265_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas265-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_265_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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

        /* ---------- "All" service-under-contract list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas265-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas265-scrim" data-list-close></div>' +
                    '<section class="vas265-panel">' +
                        '<header class="vas265-phead">' +
                            '<h2 class="vas265-ptitle">' + escapeHtml(label('VAS_265_ListTitle', 'Service Under Contract')) + '</h2>' +
                            '<span class="vas265-pcount"></span>' +
                            '<button type="button" class="vas265-close" data-list-close aria-label="' + escapeHtml(label('VAS_265_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas265-colhead">' +
                            '<span>' + escapeHtml(label('VAS_265_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas265-col-r">' + escapeHtml(label('VAS_265_ColTickets', 'Tickets')) + '</span>' +
                            '<span class="vas265-col-r">' + escapeHtml(label('VAS_265_ColAssets', 'Assets')) + '</span>' +
                        '</div>' +
                        '<div class="vas265-pbody"></div>' +
                        '<footer class="vas265-pfoot">' +
                            '<div class="vas265-pager2"></div>' +
                            '<div class="vas265-pfoot-actions">' +
                                '<button type="button" class="vas265-btn vas265-btn-ghost" data-list-close>' + escapeHtml(label('VAS_265_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas265-btn vas265-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_265_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas265-pbody');
            $listPager = $list.find('.vas265-pager2');
            $listCount = $list.find('.vas265-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas265-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas265-pgbtn', function () { turnListPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas265', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function openList() {
            listOffset = 0;
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas265-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas265-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas265-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetServiceUnderContractContracts',
                type: 'GET', dataType: 'json', cache: false,
                data: { offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas265-state">' + escapeHtml(label('VAS_265_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_265_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas265-state">' + escapeHtml(label('VAS_265_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            var warn = Number(row.OpenTicketCount || 0) >= WARN_THRESHOLD;
            return '<div class="vas265-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas265-mtc-main">' +
                    '<span class="vas265-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas265-mtc-text">' +
                        '<span class="vas265-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas265-row-meta">' + escapeHtml(row.DocumentNo || '') + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas265-col-r"><span class="vas265-chip ' + (warn ? 'vas265-chip-warn' : 'vas265-chip-info') + '"><span class="vas265-dot"></span>' + formatCount(row.OpenTicketCount) + '</span></span>' +
                '<span class="vas265-col-r"><b>' + formatCount(row.AssetCount) + '</b></span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas265-state">' + escapeHtml(label('VAS_265_Empty', 'Nothing here right now.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_265_Of', 'of');

            $listPager.html(
                '<span class="vas265-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas265-pgctl">' +
                    '<button type="button" class="vas265-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_265_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas265-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_265_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
        // to exactly this widget's population, fetched as a plain Contract_ID
        // list (GetServiceUnderContractContractIds) rather than reconstructed
        // as a raw where fragment client-side (see the file-header Scope note
        // for why).
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetServiceUnderContractContractIds',
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
                '<div class="vas265-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas265-scrim" data-detail-close></div>' +
                    '<section class="vas265-panel">' +
                        '<header class="vas265-mhead">' +
                            '<h2 class="vas265-mtitle">' + escapeHtml(label('VAS_265_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas265-mhead-meta"></span>' +
                            '<button type="button" class="vas265-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_265_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas265-mbody"></div>' +
                        '<div class="vas265-mmsg" role="status"></div>' +
                        '<footer class="vas265-mfoot">' +
                            '<button type="button" class="vas265-btn vas265-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_265_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas265-btn vas265-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_265_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas265-btn vas265-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_265_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas265-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_265_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_265_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas265-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas265-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas265-confirm-box">' +
                        '<p class="vas265-confirm-msg"></p>' +
                        '<div class="vas265-confirm-actions">' +
                            '<button type="button" class="vas265-btn vas265-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_265_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas265-btn vas265-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_265_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas265-confirm-msg');
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
            return '<div class="vas265-stat">' +
                '<span class="vas265-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas265-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
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
            $detailBody = $detail.find('.vas265-mbody');
            $detail.find('.vas265-mhead-meta').empty();
            $detailBody.html('<div class="vas265-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas265-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas265-state">' + escapeHtml(label('VAS_265_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas265-state">' + escapeHtml(label('VAS_265_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_265_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_265_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas265-attn-card">' +
                    '<span class="vas265-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas265-attn-main">' +
                        '<span class="vas265-attn-title2">' + escapeHtml(label('VAS_265_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas265-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas265-attn-empty">' + escapeHtml(label('VAS_265_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas265-attn">' +
                '<div class="vas265-attn-title">' + escapeHtml(label('VAS_265_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_265_Period', 'Period');
            var invoicedLabel = label('VAS_265_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_265_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas265-band-ok' : 'vas265-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas265-sched-row">' +
                    '<span class="vas265-sched-left">' + left + '</span>' +
                    '<span class="vas265-sched-right">' +
                        '<span class="vas265-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas265-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas265-sched">' +
                '<div class="vas265-sched-title">' + escapeHtml(label('VAS_265_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas265-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas265-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_265_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_265_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas265-dtop2">' +
                    '<span class="vas265-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas265-dhead-main">' +
                        '<span class="vas265-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas265-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas265-dhead-right">' +
                        '<span class="vas265-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas265-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas265-stats">' +
                    statHtml('VAS_265_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_265_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_265_Type', 'Type', row.ContractType) +
                    statHtml('VAS_265_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_265_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_265_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_265_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_265_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
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
                showDetailMessage(label('VAS_265_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_265_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadBody();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_265_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas265-modal-open'); }
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
            $(document).off('keydown.MPCvas265');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas265-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_265_ServiceUnderContractWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_265_ServiceUnderContractWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_265_ServiceUnderContractWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_265_ServiceUnderContractWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_265_ServiceUnderContractWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_265_ServiceUnderContractWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
