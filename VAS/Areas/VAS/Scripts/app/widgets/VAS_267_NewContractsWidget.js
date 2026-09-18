/**
 * VAS_267 New Contracts · This Quarter Widget (Service Contracts dashboard)
 * Purpose - c3 x r2 portfolio-growth widget: completed contracts (Processed=
 *           'Y') whose StartDate falls within the last rolling quarter
 *           (DAYSBETWEEN(StartDate, CURRENT_DATE) BETWEEN 0 AND 90), newest
 *           start first - lets the contracts manager see fresh portfolio
 *           additions and the value they add. Header sub-line = "N started
 *           ≤90d · $V added"; the header "All ->" link opens the full list
 *           modal. Each row shows a leading calm-green "zap" icon tile
 *           (newness, not alarm), the customer (title), a coverage meta-line
 *           ("product · started Nd ago · ContractType") and a right-aligned
 *           value ("value" caption underneath). Read-only drill - no per-row
 *           action button. Row -> the shared contract detail modal (reusing
 *           VAS_241_ContractSearchWidget's GetContract / RunRenew /
 *           RunGenerateInvoice endpoints, the same cross-widget endpoint-
 *           reuse pattern VAS_120 set with VAS_126 and
 *           VAS_244/245/246/258/259/264/265/266 already reused too), whose
 *           "Renew"/"Generate invoice" run the real processes and "Open
 *           record" zooms to the Service Contract window. The widget never
 *           resizes on paging - fixed c3 x r2 footprint, outer overflow
 *           hidden, a full page shows all 4 rows with no clipping.
 * Design  - new-contracts.html (the parent Service Contracts dashboard mock,
 *           single-widget preview of newContractsWidget()) is the pixel-
 *           level source of truth; re-created verbatim below (tokens,
 *           green-tinted leading zap-icon tile, header icon well/title/sub/
 *           "All ->" link, row structure, pager). The "All" list modal
 *           shares the SAME 5-column shape (Contract / Value / Ends /
 *           Renewal / Status) every other Service-Contracts drill list in
 *           this dashboard uses - VAS_266's own list-modal fix (2026-09-09)
 *           established that the combined dashboard mock's shared
 *           openListItems()/renderList() always renders that shape
 *           regardless of which widget's "All" link opened it, so this
 *           widget's own body-row query already carries RenewalType/EndDate/
 *           DocStatus/IsCancel too, not just what the compact body row
 *           itself needs.
 * Scope   - "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly this widget's new-
 *           contract population, fetched as a plain Contract_ID list
 *           (GetNewContractsContractIds) rather than reconstructed as a raw
 *           where fragment client-side (this codebase's own VAS_140 widget
 *           shows that inlining relative date arithmetic into a raw where-
 *           clause breaks across Postgres/Oracle). When hosted (windowNo >=
 *           0) this goes through widgetFirevalueChanged / ActionName - the
 *           same channel "Open record" already uses - resolved by NAME
 *           through the host window framework
 *           (VAS_244/245/246/258/259/264/265/266's own openInBrowser fix,
 *           2026-09-08: VAS.ZoomUtil's hardcoded AD_Window_ID 1000248 turned
 *           out to resolve to a different window (Lead) on the real install).
 *
 * Backend - VAS_267_NewContractsWidget/GetNewContractsSummary     (GET -> NewCount, NewValueBase, currency)
 *           VAS_267_NewContractsWidget/GetNewContractsContracts   (GET offset,limit -> rows + total)
 *           VAS_267_NewContractsWidget/GetNewContractsContractIds (GET -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract               (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                  (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice        (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | New Contracts · This Quarter          | VAS_267_Title
 *  2  | {n} started ≤90d                      | VAS_267_SubCount
 *  3  | added                                 | VAS_267_SubAdded
 *  4  | All                                   | VAS_267_All
 *  5  | Couldn't load                         | VAS_267_LoadError
 *  6  | New Contracts This Quarter (list title) | VAS_267_ListTitle
 *  7  | contracts                             | VAS_267_ContractsCount
 *  8  | Contract                              | VAS_267_ColContract
 *  9  | Value                                 | VAS_267_ColValue
 * 10  | Ends                                  | VAS_267_ColEnds
 * 11  | Renewal                               | VAS_267_ColRenewal
 * 12  | Status                                | VAS_267_ColStatus
 * 13  | of                                    | VAS_267_Of
 * 14  | Previous page                         | VAS_267_PrevPage
 * 15  | Next page                             | VAS_267_NextPage
 * 16  | Nothing here right now.               | VAS_267_Empty
 * 17  | Unable to load contracts.             | VAS_267_UnableToLoad
 * 18  | Close                                 | VAS_267_Close
 * 19  | Open in browser                       | VAS_267_OpenInBrowser
 * 20  | Service Contract                      | VAS_267_ContractTitle
 * 21  | Open record                           | VAS_267_OpenRecord
 * 22  | rep                                   | VAS_267_Rep
 * 23  | cycles                                | VAS_267_Cycles
 * 24  | Contract cycle value                  | VAS_267_Value
 * 25  | Status                                | VAS_267_Status
 * 26  | Type                                  | VAS_267_Type
 * 27  | Renewal                               | VAS_267_Renewal
 * 28  | Ends                                  | VAS_267_Ends
 * 29  | in {n}d                               | VAS_267_EndsIn
 * 30  | Ended {n}d ago                        | VAS_267_EndedAgo
 * 31  | Notice days                           | VAS_267_NoticeDays
 * 32  | Billed amount                         | VAS_267_Billed
 * 33  | Unbilled amount                       | VAS_267_Unbilled
 * 34  | What needs attention                  | VAS_267_NeedsAttention
 * 35  | No open actions - contract is healthy.| VAS_267_NoOpenActions
 * 36  | Billing overdue                       | VAS_267_BillingOverdue
 * 37  | Next period {n}d overdue              | VAS_267_NextPeriodOverdue
 * 38  | unbilled total                        | VAS_267_UnbilledTotal
 * 39  | open tickets for this customer        | VAS_267_OpenTickets
 * 40  | Billing schedule                      | VAS_267_BillingSchedule
 * 41  | Period                                | VAS_267_Period
 * 42  | Invoiced                              | VAS_267_Invoiced
 * 43  | Pending                               | VAS_267_SchedulePending
 * 44  | Generate invoice                      | VAS_267_GenerateInvoice
 * 45  | Renew                                 | VAS_267_Renew
 * 46  | Run RenewContract for this contract now? | VAS_267_ConfirmRenew
 * 47  | Generate invoices for the overdue billing periods now? | VAS_267_ConfirmGenerateInvoice
 * 48  | Working…                              | VAS_267_Working
 * 49  | The action failed.                    | VAS_267_ActionFailed
 * 50  | Couldn't load this contract.          | VAS_267_DetailLoadError
 * 51  | started {n}d ago                      | VAS_267_StartedAgo
 * 52  | value                                 | VAS_267_ValueCaption
 * 53  | d                                     | VAS_267_DaysSuffix
 * 54  | d ago                                 | VAS_267_DaysAgoSuffix
 * 55  | Active                                | VAS_267_StatusActive
 * 56  | Expiring                              | VAS_267_StatusExpiring
 * 57  | Ended                                 | VAS_267_StatusEnded
 * 58  | Cancelled                             | VAS_267_StatusCancelled
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_267_NewContractsWidget/';

    var BODY_PAGE = 4;
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

    // Detail-modal "Ends" stat (Sentence case, matching VAS_241/244/245/246/258/259/264/265/266).
    function endsText(days) {
        var d = Number(days || 0);
        if (d < 0) { return label('VAS_267_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_267_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    // Row meta "started Nd ago" (new-contracts.prompt.md §1/§7).
    function startedAgoText(days) {
        return label('VAS_267_StartedAgo', 'started {n}d ago').replace('{n}', formatCount(days));
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas267-band-ok'; }
        if (code === 'EXPIRING') { return 'vas267-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas267-band-danger'; }
        return 'vas267-band-info';
    }

    function statusText(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'EXPIRING') { return label('VAS_267_StatusExpiring', 'Expiring'); }
        if (code === 'ENDED') { return label('VAS_267_StatusEnded', 'Ended'); }
        if (code === 'CANCELLED') { return label('VAS_267_StatusCancelled', 'Cancelled'); }
        return label('VAS_267_StatusActive', 'Active');
    }

    // Renewal chip - dot colour keyed off the server-resolved code (A =
    // green, else amber), text = the server-decoded AD_Ref_List label
    // (never a hardcoded "Auto"/"Manual").
    function renewalChipHtml(code, renewalLabel) {
        var isAuto = String(code || '').toUpperCase() === 'A';
        return '<span class="vas267-chip ' + (isAuto ? 'vas267-chip-auto' : 'vas267-chip-manual') + '">' +
            '<span class="vas267-dot"></span>' + escapeHtml(renewalLabel || code || '') +
        '</span>';
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
        if (name === 'zap') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon></svg>';
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
    // .vas267-root's own clamp() font-size formulas) from the actual dashboard
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

    VAS.VAS_267_NewContractsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas267-root">');
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
                '<div class="vas267-head">' +
                    '<div class="vas267-head-l">' +
                        '<span class="vas267-iconwell">' + icon('zap') + '</span>' +
                        '<span class="vas267-head-text">' +
                            '<span class="vas267-title">' + escapeHtml(label('VAS_267_Title', 'New Contracts · This Quarter')) + '</span>' +
                            '<span class="vas267-sub vas267-skel-sub">&nbsp;</span>' +
                        '</span>' +
                    '</div>' +
                    '<button type="button" class="vas267-alllink" data-open-list>' + escapeHtml(label('VAS_267_All', 'All')) + ' ' + icon('chev') + '</button>' +
                '</div>' +
                '<div class="vas267-body">' +
                    '<div class="vas267-list"></div>' +
                    '<div class="vas267-pager"></div>' +
                '</div>'
            );
            $body = $root.find('.vas267-body');
            $bodyList = $root.find('.vas267-list');
            $bodyPager = $root.find('.vas267-pager');
            $root.on('click', '[data-open-list]', function () { openList(); });
            $root.on('click', '.vas267-row', function () { openDetail(Number($(this).attr('data-cid'))); });
            $root.on('click', '.vas267-pgbtn', function () { turnBodyPage($(this).attr('data-dir')); });
        }

        function subLine(count, valueText) {
            return label('VAS_267_SubCount', '{n} started ≤90d').replace('{n}', formatCount(count)) + ' · ' +
                valueText + ' ' + label('VAS_267_SubAdded', 'added');
        }

        function renderBodyError() {
            $root.find('.vas267-sub').removeClass('vas267-skel-sub').text(label('VAS_267_LoadError', "Couldn't load"));
            $bodyList.html('<div class="vas267-state">' + escapeHtml(label('VAS_267_UnableToLoad', 'Unable to load contracts.')) + '</div>');
            $bodyPager.empty();
        }

        function loadBody() {
            var seq = ++bodySeq;
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetNewContractsContracts',
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

        // Sub-line count/value is a small separate call so paging the body
        // list never re-fetches the (identical) header aggregate.
        function loadSummaryValue() {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetNewContractsSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { return; }
                    var valueText = formatMoney(parsed.NewValueBase, parsed.CurrencyIso, parsed.CurrencySymbol, parsed.CurrencyPrecision);
                    $root.find('.vas267-sub').removeClass('vas267-skel-sub').text(subLine(parsed.NewCount, valueText));
                },
                error: function () { /* sub-line just stays as last known value */ }
            });
        }

        function rowHtml(row) {
            var meta = [row.ProductName, startedAgoText(row.StartedDaysAgo), row.ContractTypeLabel].filter(function (p) { return p; }).join(' · ');
            return '<div class="vas267-row" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas267-ic">' + icon('zap') + '</span>' +
                '<span class="vas267-row-main">' +
                    '<span class="vas267-row-name" title="' + escapeHtml(row.CustomerName || '') + '">' + escapeHtml(row.CustomerName || '') + '</span>' +
                    '<span class="vas267-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas267-row-val">' +
                    '<span class="vas267-row-amt">' + escapeHtml(formatMoney(row.GrandTotalBase, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                    '<span class="vas267-row-vcap">' + escapeHtml(label('VAS_267_ValueCaption', 'value')) + '</span>' +
                '</span>' +
            '</div>';
        }

        function renderBody(rows) {
            if (!rows.length) {
                $bodyList.html('<div class="vas267-empty">' + icon('check') + '<span>' + escapeHtml(label('VAS_267_Empty', 'Nothing here right now.')) + '</span></div>');
                $bodyPager.empty();
                return;
            }

            $bodyList.html(rows.map(rowHtml).join(''));

            var pages = Math.max(1, Math.ceil(bodyTotal / BODY_PAGE));
            if (pages <= 1) { $bodyPager.empty(); return; }

            var current = Math.floor(bodyOffset / BODY_PAGE);
            var start = bodyOffset + 1, end = bodyOffset + rows.length;
            var of = label('VAS_267_Of', 'of');

            $bodyPager.html(
                '<span class="vas267-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(bodyTotal)) + '</span>' +
                '<span class="vas267-pgctl">' +
                    '<button type="button" class="vas267-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_267_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas267-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_267_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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

        /* ---------- "All" new-contracts list modal (shared 5-col shape) ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas267-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas267-scrim" data-list-close></div>' +
                    '<section class="vas267-panel">' +
                        '<header class="vas267-phead">' +
                            '<h2 class="vas267-ptitle">' + escapeHtml(label('VAS_267_ListTitle', 'New Contracts This Quarter')) + '</h2>' +
                            '<span class="vas267-pcount"></span>' +
                            '<button type="button" class="vas267-close" data-list-close aria-label="' + escapeHtml(label('VAS_267_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas267-colhead">' +
                            '<span>' + escapeHtml(label('VAS_267_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas267-col-r">' + escapeHtml(label('VAS_267_ColValue', 'Value')) + '</span>' +
                            '<span class="vas267-col-r">' + escapeHtml(label('VAS_267_ColEnds', 'Ends')) + '</span>' +
                            '<span class="vas267-col-r">' + escapeHtml(label('VAS_267_ColRenewal', 'Renewal')) + '</span>' +
                            '<span class="vas267-col-r">' + escapeHtml(label('VAS_267_ColStatus', 'Status')) + '</span>' +
                        '</div>' +
                        '<div class="vas267-pbody"></div>' +
                        '<footer class="vas267-pfoot">' +
                            '<div class="vas267-pager2"></div>' +
                            '<div class="vas267-pfoot-actions">' +
                                '<button type="button" class="vas267-btn vas267-btn-ghost" data-list-close>' + escapeHtml(label('VAS_267_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas267-btn vas267-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_267_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas267-pbody');
            $listPager = $list.find('.vas267-pager2');
            $listCount = $list.find('.vas267-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas267-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas267-pgbtn', function () { turnListPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas267', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function openList() {
            listOffset = 0;
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas267-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas267-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas267-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetNewContractsContracts',
                type: 'GET', dataType: 'json', cache: false,
                data: { offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas267-state">' + escapeHtml(label('VAS_267_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_267_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas267-state">' + escapeHtml(label('VAS_267_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            return '<div class="vas267-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas267-mtc-main">' +
                    '<span class="vas267-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas267-mtc-text">' +
                        '<span class="vas267-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas267-row-meta">' + escapeHtml([row.DocumentNo, row.ProductName].filter(function (p) { return p; }).join(' · ')) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas267-col-r"><b>' + escapeHtml(formatMoney(row.GrandTotalBase, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</b></span>' +
                '<span class="vas267-col-r">' + escapeHtml(Number(row.DaysToEnd || 0) < 0 ? formatCount(-row.DaysToEnd) + label('VAS_267_DaysAgoSuffix', 'd ago') : formatCount(row.DaysToEnd) + label('VAS_267_DaysSuffix', 'd')) + '</span>' +
                '<span class="vas267-col-r">' + renewalChipHtml(row.RenewalTypeCode, row.RenewalTypeLabel) + '</span>' +
                '<span class="vas267-col-r"><span class="vas267-band ' + bandClass(row.LifecycleStatusCode) + '">' + escapeHtml(statusText(row.LifecycleStatusCode)) + '</span></span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas267-state">' + escapeHtml(label('VAS_267_Empty', 'Nothing here right now.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_267_Of', 'of');

            $listPager.html(
                '<span class="vas267-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas267-pgctl">' +
                    '<button type="button" class="vas267-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_267_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas267-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_267_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
        // to exactly this widget's new-contract population, fetched as a plain
        // Contract_ID list (GetNewContractsContractIds) rather than
        // reconstructed as a raw where fragment client-side (see the
        // file-header Scope note for why).
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetNewContractsContractIds',
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
                '<div class="vas267-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas267-scrim" data-detail-close></div>' +
                    '<section class="vas267-panel">' +
                        '<header class="vas267-mhead">' +
                            '<h2 class="vas267-mtitle">' + escapeHtml(label('VAS_267_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas267-mhead-meta"></span>' +
                            '<button type="button" class="vas267-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_267_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas267-mbody"></div>' +
                        '<div class="vas267-mmsg" role="status"></div>' +
                        '<footer class="vas267-mfoot">' +
                            '<button type="button" class="vas267-btn vas267-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_267_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas267-btn vas267-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_267_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas267-btn vas267-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_267_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas267-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_267_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_267_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas267-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas267-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas267-confirm-box">' +
                        '<p class="vas267-confirm-msg"></p>' +
                        '<div class="vas267-confirm-actions">' +
                            '<button type="button" class="vas267-btn vas267-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_267_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas267-btn vas267-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_267_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas267-confirm-msg');
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
            return '<div class="vas267-stat">' +
                '<span class="vas267-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas267-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
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
            $detailBody = $detail.find('.vas267-mbody');
            $detail.find('.vas267-mhead-meta').empty();
            $detailBody.html('<div class="vas267-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas267-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas267-state">' + escapeHtml(label('VAS_267_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas267-state">' + escapeHtml(label('VAS_267_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_267_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_267_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas267-attn-card">' +
                    '<span class="vas267-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas267-attn-main">' +
                        '<span class="vas267-attn-title2">' + escapeHtml(label('VAS_267_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas267-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas267-attn-empty">' + escapeHtml(label('VAS_267_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas267-attn">' +
                '<div class="vas267-attn-title">' + escapeHtml(label('VAS_267_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_267_Period', 'Period');
            var invoicedLabel = label('VAS_267_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_267_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas267-band-ok' : 'vas267-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas267-sched-row">' +
                    '<span class="vas267-sched-left">' + left + '</span>' +
                    '<span class="vas267-sched-right">' +
                        '<span class="vas267-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas267-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas267-sched">' +
                '<div class="vas267-sched-title">' + escapeHtml(label('VAS_267_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas267-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas267-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_267_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_267_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas267-dtop2">' +
                    '<span class="vas267-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas267-dhead-main">' +
                        '<span class="vas267-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas267-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas267-dhead-right">' +
                        '<span class="vas267-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas267-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas267-stats">' +
                    statHtml('VAS_267_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_267_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_267_Type', 'Type', row.ContractType) +
                    statHtml('VAS_267_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_267_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_267_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_267_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_267_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
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
                showDetailMessage(label('VAS_267_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_267_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadBody();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_267_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas267-modal-open'); }
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
            $(document).off('keydown.MPCvas267');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas267-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_267_NewContractsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_267_NewContractsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_267_NewContractsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_267_NewContractsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_267_NewContractsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_267_NewContractsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
