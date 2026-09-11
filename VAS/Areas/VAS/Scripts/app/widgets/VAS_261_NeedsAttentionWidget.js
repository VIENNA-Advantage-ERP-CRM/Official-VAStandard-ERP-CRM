/**
 * VAS_261 Needs Attention Widget (Service Contracts dashboard)
 * Purpose - c3 x r2 read-only 2x2 tile widget that protects renewals and
 *           billing: (a) Manual renewal due · (b) Notice overdue · (c) Draft
 *           not activated · (d) Billing overdue. Each tile shows a tinted
 *           icon, a big count, a label and an "Open list ->" affordance;
 *           clicking a tile opens that condition's contract list modal (paged
 *           @7, most-urgent first). Header sub-line = "Protect renewals &
 *           billing". Row -> the shared contract detail modal (reusing
 *           VAS_241_ContractSearchWidget's GetContract / RunRenew /
 *           RunGenerateInvoice endpoints, the same cross-widget endpoint-reuse
 *           pattern VAS_120 set with VAS_126 and VAS_244/245/246/258/259/260
 *           already reused too), whose "Renew" runs the real RenewContract
 *           process, "Generate invoice" runs CreateContractInvoice, and "Open
 *           record" zooms to the Service Contract window. The widget never
 *           resizes - fixed c3 x r2 footprint, the 2x2 grid fills the body.
 * Design  - needs-attention.html (the parent Service Contracts dashboard mock,
 *           single-widget preview of needsWidget()) is the pixel-level source
 *           of truth; re-created verbatim below (tokens, na-grid/na tile
 *           structure, header icon well/title/sub, no "All ->" link - drill is
 *           per-tile).
 * Scope   - "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly the clicked tile's
 *           population, fetched as a plain Contract_ID list
 *           (GetNeedsAttentionIds) rather than reconstructed as a raw where
 *           fragment client-side (this codebase's own VAS_140 widget shows
 *           that inlining relative date arithmetic into a raw where-clause
 *           breaks across Postgres/Oracle). When hosted (windowNo >= 0) this
 *           goes through widgetFirevalueChanged / ActionName - the same
 *           channel "Open record" already uses - resolved by NAME through the
 *           host window framework (VAS_244/245/246/258/259/260's own
 *           openInBrowser fix, 2026-09-08: VAS.ZoomUtil's hardcoded
 *           AD_Window_ID 1000248 turned out to resolve to a different window
 *           (Lead) on the real install). "Live"/"draft" read Processed
 *           instead of DocStatus, the same fix already applied to VAS_243/244/
 *           245/246/258/259/260 (see the controller's own header note).
 *
 * Backend - VAS_261_NeedsAttentionWidget/GetNeedsAttentionSummary (GET -> four tile counts)
 *           VAS_261_NeedsAttentionWidget/GetNeedsAttentionDrill    (GET tile,offset,limit -> rows + total)
 *           VAS_261_NeedsAttentionWidget/GetNeedsAttentionIds      (GET tile -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract                (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                   (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice         (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Needs Attention                       | VAS_261_Title
 *  2  | Protect renewals & billing            | VAS_261_SubLine
 *  3  | Manual Renewal Due                    | VAS_261_TileManualDue
 *  4  | Notice Overdue                        | VAS_261_TileNoticeOverdue
 *  5  | Draft Not Activated                   | VAS_261_TileDraft
 *  6  | Billing Overdue                       | VAS_261_TileBillingOverdue
 *  7  | Open list                             | VAS_261_OpenList
 *  8  | Couldn't load                         | VAS_261_LoadError
 *  9  | contracts                             | VAS_261_ContractsCount
 * 10  | Contract                              | VAS_261_ColContract
 * 11  | Value                                 | VAS_261_ColValue
 * 12  | Ends                                  | VAS_261_ColEnds
 * 13  | Renewal                               | VAS_261_ColRenewal
 * 14  | Status                                | VAS_261_ColStatus
 * 15  | of                                    | VAS_261_Of
 * 16  | Previous page                         | VAS_261_PrevPage
 * 17  | Next page                             | VAS_261_NextPage
 * 18  | No contracts.                         | VAS_261_Empty
 * 19  | Unable to load contracts.             | VAS_261_UnableToLoad
 * 20  | Close                                 | VAS_261_Close
 * 21  | Open in browser                       | VAS_261_OpenInBrowser
 * 22  | Service Contract                      | VAS_261_ContractTitle
 * 23  | Open record                           | VAS_261_OpenRecord
 * 24  | rep                                   | VAS_261_Rep
 * 25  | cycles                                | VAS_261_Cycles
 * 26  | Contract cycle value                  | VAS_261_Value
 * 27  | Status                                | VAS_261_Status
 * 28  | Type                                  | VAS_261_Type
 * 29  | Renewal                               | VAS_261_Renewal
 * 30  | Ends                                  | VAS_261_Ends
 * 31  | in {n}d                               | VAS_261_EndsIn
 * 32  | Ended {n}d ago                        | VAS_261_EndedAgo
 * 33  | Notice days                           | VAS_261_NoticeDays
 * 34  | Billed amount                         | VAS_261_Billed
 * 35  | Unbilled amount                       | VAS_261_Unbilled
 * 36  | What needs attention                  | VAS_261_NeedsAttention
 * 37  | No open actions - contract is healthy.| VAS_261_NoOpenActions
 * 38  | Renewal notice overdue                | VAS_261_RenewalNoticeOverdue
 * 39  | Renewal notice due today              | VAS_261_RenewalNoticeDueToday
 * 40  | Manual renewal                        | VAS_261_ManualRenewal
 * 41  | notice {n}d overdue                   | VAS_261_NoticeOverdueBy
 * 42  | ends                                  | VAS_261_EndsPrefix
 * 43  | Billing overdue                       | VAS_261_BillingOverdue
 * 44  | Next period {n}d overdue              | VAS_261_NextPeriodOverdue
 * 45  | unbilled total                        | VAS_261_UnbilledTotal
 * 46  | open tickets for this customer        | VAS_261_OpenTickets
 * 47  | Billing schedule                      | VAS_261_BillingSchedule
 * 48  | Period                                | VAS_261_Period
 * 49  | Invoiced                              | VAS_261_Invoiced
 * 50  | Pending                               | VAS_261_SchedulePending
 * 51  | Generate invoice                      | VAS_261_GenerateInvoice
 * 52  | Renew                                 | VAS_261_Renew
 * 53  | Run RenewContract for this contract now? | VAS_261_ConfirmRenew
 * 54  | Generate invoices for the overdue billing periods now? | VAS_261_ConfirmGenerateInvoice
 * 55  | Working…                              | VAS_261_Working
 * 56  | The action failed.                    | VAS_261_ActionFailed
 * 57  | Couldn't load this contract.          | VAS_261_DetailLoadError
 * 58  | d (days-to-end suffix)                | VAS_261_DaysSuffix
 * 58a | d ago (past days-to-end suffix)       | VAS_261_DaysAgoSuffix
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_261_NeedsAttentionWidget/';

    var LIST_PAGE = 7;

    // Tile order, accent colour and message key - fixed 2x2 grid (needs-
    // attention.queries.md D.1's four independently-derived conditions).
    var TILES = [
        { key: 'ManualDue', icon: 'clock', color: '#D78B10', labelKey: 'VAS_261_TileManualDue', fallback: 'Manual Renewal Due', countProp: 'ManualRenewalDue' },
        { key: 'NoticeOverdue', icon: 'alert', color: '#D8434A', labelKey: 'VAS_261_TileNoticeOverdue', fallback: 'Notice Overdue', countProp: 'NoticeOverdue' },
        { key: 'Draft', icon: 'file', color: '#1F83FF', labelKey: 'VAS_261_TileDraft', fallback: 'Draft Not Activated', countProp: 'DraftNotActivated' },
        { key: 'BillingOverdue', icon: 'receipt', color: '#5F4AA6', labelKey: 'VAS_261_TileBillingOverdue', fallback: 'Billing Overdue', countProp: 'BillingOverdue' }
    ];

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
        if (d < 0) { return label('VAS_261_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_261_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    function isManualRenewal(code) {
        var c = String(code || '').toUpperCase();
        return c === 'M' || c === 'MNL';
    }

    function renewalClass(code) {
        var c = String(code || '').toUpperCase();
        if (c === 'A' || c === 'ATC') { return 'vas261-renewal-auto'; }
        if (c === 'M' || c === 'MNL') { return 'vas261-renewal-manual'; }
        return 'vas261-renewal-neutral';
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas261-band-ok'; }
        if (code === 'EXPIRING') { return 'vas261-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas261-band-danger'; }
        return 'vas261-band-info';
    }

    // Drill row Status pill colour - the row itself carries only the raw
    // DocStatus text (no per-row LifecycleStatusCode field), but every row in
    // one tile's drill shares the same lifecycle band by construction (each
    // tile's WHERE clause already is that band), so the CLICKED TILE decides
    // the colour, same as VAS_260's bandTagClass(currentBand).
    function tileBandClass(tileKey) {
        var k = String(tileKey || '').toUpperCase();
        if (k === 'DRAFT') { return 'vas261-band-info'; }
        return 'vas261-band-danger';
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
        if (name === 'arrow') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>';
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
        if (name === 'bell') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path><path d="M13.73 21a2 2 0 0 1-3.46 0"></path></svg>';
        }
        if (name === 'clock') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>';
        }
        if (name === 'alert') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m21.73 18-8-14a2 2 0 0 0-3.46 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>';
        }
        if (name === 'file') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><path d="M14 2v6h6"></path></svg>';
        }
        if (name === 'receipt') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1z"></path><path d="M8 7h8M8 11h8M8 15h5"></path></svg>';
        }
        return '';
    }

    // Populates --dash-inline-size (a shared, dashboard-wide CSS var read by
    // .vas261-root's own clamp() font-size formula) from the actual dashboard
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

    VAS.VAS_261_NeedsAttentionWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas261-root">');
        var $grid;

        var currentTile = '';

        var $list, $listBody, $listPager, $listCount;
        var listOffset = 0, listTotal = 0, listSeq = 0;

        var $detail, $detailBody, $detailMsg;
        var detailBusy = false;

        var $confirm, $confirmMsg;
        var confirmCallback = null;

        var zoomWindowId = ZOOM_WINDOW_ID;

        /* ---------- Widget header + 2x2 tile grid ---------- */

        function createWidget() {
            $root.html(
                '<div class="vas261-head">' +
                    '<span class="vas261-iconwell">' + icon('bell') + '</span>' +
                    '<span class="vas261-head-text">' +
                        '<span class="vas261-title">' + escapeHtml(label('VAS_261_Title', 'Needs Attention')) + '</span>' +
                        '<span class="vas261-sub">' + escapeHtml(label('VAS_261_SubLine', 'Protect renewals & billing')) + '</span>' +
                    '</span>' +
                '</div>' +
                '<div class="vas261-body"><div class="vas261-grid"></div></div>'
            );
            $grid = $root.find('.vas261-grid');
            $grid.html(TILES.map(tileHtml).join(''));
            $root.on('click', '[data-open-tile]', function () { openList($(this).attr('data-open-tile')); });
        }

        function tileHtml(tile) {
            return '<button type="button" class="vas261-tile" data-open-tile="' + tile.key + '">' +
                '<span class="vas261-tile-top">' +
                    '<span class="vas261-tile-ic" style="background:' + tile.color + '1f;color:' + tile.color + '">' + icon(tile.icon) + '</span>' +
                    '<span class="vas261-tile-n vas261-skel-n" data-count-for="' + tile.key + '">&nbsp;</span>' +
                '</span>' +
                '<span class="vas261-tile-l">' + escapeHtml(label(tile.labelKey, tile.fallback)) + '</span>' +
                '<span class="vas261-tile-go">' + escapeHtml(label('VAS_261_OpenList', 'Open list')) + ' ' + icon('arrow') + '</span>' +
            '</button>';
        }

        function renderError() {
            $grid.find('.vas261-tile-n').removeClass('vas261-skel-n').text('—');
        }

        function renderSummary(data) {
            TILES.forEach(function (tile) {
                var count = Number(data[tile.countProp] || 0);
                $grid.find('[data-count-for="' + tile.key + '"]').removeClass('vas261-skel-n').text(formatCount(count));
            });
        }

        function loadSummary() {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetNeedsAttentionSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { renderError(); return; }
                    renderSummary(parsed);
                },
                error: function () { renderError(); }
            });
        }

        /* ---------- Per-tile drill list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas261-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas261-scrim" data-list-close></div>' +
                    '<section class="vas261-panel">' +
                        '<header class="vas261-phead">' +
                            '<h2 class="vas261-ptitle"></h2>' +
                            '<span class="vas261-pcount"></span>' +
                            '<button type="button" class="vas261-close" data-list-close aria-label="' + escapeHtml(label('VAS_261_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas261-colhead">' +
                            '<span>' + escapeHtml(label('VAS_261_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas261-col-r">' + escapeHtml(label('VAS_261_ColValue', 'Value')) + '</span>' +
                            '<span class="vas261-col-r">' + escapeHtml(label('VAS_261_ColEnds', 'Ends')) + '</span>' +
                            '<span>' + escapeHtml(label('VAS_261_ColRenewal', 'Renewal')) + '</span>' +
                            '<span class="vas261-col-r">' + escapeHtml(label('VAS_261_ColStatus', 'Status')) + '</span>' +
                        '</div>' +
                        '<div class="vas261-pbody"></div>' +
                        '<footer class="vas261-pfoot">' +
                            '<div class="vas261-pager"></div>' +
                            '<div class="vas261-pfoot-actions">' +
                                '<button type="button" class="vas261-btn vas261-btn-ghost" data-list-close>' + escapeHtml(label('VAS_261_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas261-btn vas261-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_261_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas261-pbody');
            $listPager = $list.find('.vas261-pager');
            $listCount = $list.find('.vas261-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas261-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas261-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas261', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function tileTitle(tileKey) {
            var found = TILES.filter(function (t) { return t.key === tileKey; })[0];
            return found ? label(found.labelKey, found.fallback) : tileKey;
        }

        function openList(tileKey) {
            currentTile = tileKey;
            listOffset = 0;
            $list.find('.vas261-ptitle').text(tileTitle(tileKey));
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas261-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas261-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas261-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetNeedsAttentionDrill',
                type: 'GET', dataType: 'json', cache: false,
                data: { tile: currentTile, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas261-state">' + escapeHtml(label('VAS_261_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_261_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas261-state">' + escapeHtml(label('VAS_261_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            return '<div class="vas261-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas261-mtc-main">' +
                    '<span class="vas261-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas261-mtc-text">' +
                        '<span class="vas261-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas261-row-meta">' + escapeHtml([row.DocumentNo, row.ProductName].filter(function (p) { return p; }).join(' · ')) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas261-col-r"><b>' + escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</b></span>' +
                '<span class="vas261-col-r">' + escapeHtml(Number(row.DaysToEnd || 0) < 0 ? formatCount(-row.DaysToEnd) + label('VAS_261_DaysAgoSuffix', 'd ago') : formatCount(row.DaysToEnd) + label('VAS_261_DaysSuffix', 'd')) + '</span>' +
                '<span><span class="vas261-renewal ' + renewalClass(row.RenewalTypeCode) + '"><span class="vas261-dot"></span>' + escapeHtml(row.RenewalType || '') + '</span></span>' +
                '<span class="vas261-col-r"><span class="vas261-band ' + tileBandClass(currentTile) + '">' + escapeHtml(row.DocStatus || '') + '</span></span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas261-state">' + escapeHtml(label('VAS_261_Empty', 'No contracts.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_261_Of', 'of');

            $listPager.html(
                '<span class="vas261-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas261-pgctl">' +
                    '<button type="button" class="vas261-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_261_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas261-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_261_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>'
            );
        }

        function turnPage(direction) {
            var next = listOffset + (direction === 'next' ? LIST_PAGE : -LIST_PAGE);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            listOffset = next;
            loadList();
        }

        // "Open in browser": zooms to the Service Contract window filtered down
        // to exactly the clicked tile's population, fetched as a plain Contract_ID
        // list (GetNeedsAttentionIds) rather than reconstructed as a raw where
        // fragment client-side (see the file-header Scope note for why).
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetNeedsAttentionIds',
                        type: 'GET', dataType: 'json', cache: false,
                        data: { tile: currentTile },
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
                '<div class="vas261-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas261-scrim" data-detail-close></div>' +
                    '<section class="vas261-panel">' +
                        '<header class="vas261-mhead">' +
                            '<h2 class="vas261-mtitle">' + escapeHtml(label('VAS_261_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas261-mhead-meta"></span>' +
                            '<button type="button" class="vas261-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_261_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas261-mbody"></div>' +
                        '<div class="vas261-mmsg" role="status"></div>' +
                        '<footer class="vas261-mfoot">' +
                            '<button type="button" class="vas261-btn vas261-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_261_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas261-btn vas261-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_261_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas261-btn vas261-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_261_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas261-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_261_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_261_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas261-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas261-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas261-confirm-box">' +
                        '<p class="vas261-confirm-msg"></p>' +
                        '<div class="vas261-confirm-actions">' +
                            '<button type="button" class="vas261-btn vas261-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_261_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas261-btn vas261-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_261_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas261-confirm-msg');
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
            return '<div class="vas261-stat">' +
                '<span class="vas261-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas261-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
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
            $detailBody = $detail.find('.vas261-mbody');
            $detail.find('.vas261-mhead-meta').empty();
            $detailBody.html('<div class="vas261-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas261-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas261-state">' + escapeHtml(label('VAS_261_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas261-state">' + escapeHtml(label('VAS_261_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            if (isManualRenewal(row.RenewalTypeCode) && !row.HasSuccessor && row.NoticeSlackDays != null && row.NoticeSlackDays <= 0) {
                var noticeTitle = row.NoticeSlackDays < 0
                    ? label('VAS_261_RenewalNoticeOverdue', 'Renewal notice overdue')
                    : label('VAS_261_RenewalNoticeDueToday', 'Renewal notice due today');
                var noticeDescParts = [label('VAS_261_ManualRenewal', 'Manual renewal')];
                if (row.NoticeSlackDays < 0) {
                    noticeDescParts.push(label('VAS_261_NoticeOverdueBy', 'notice {n}d overdue').replace('{n}', formatCount(-row.NoticeSlackDays)));
                }
                noticeDescParts.push(label('VAS_261_EndsPrefix', 'ends') + ' ' + endsText(row.DaysToEnd));
                cards += '<div class="vas261-attn-card">' +
                    '<span class="vas261-attn-ic vas261-attn-ic-danger">' + icon('warning') + '</span>' +
                    '<span class="vas261-attn-main">' +
                        '<span class="vas261-attn-title2">' + escapeHtml(noticeTitle) + '</span>' +
                        '<span class="vas261-attn-desc">' + escapeHtml(noticeDescParts.join(' · ')) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_261_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_261_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas261-attn-card">' +
                    '<span class="vas261-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas261-attn-main">' +
                        '<span class="vas261-attn-title2">' + escapeHtml(label('VAS_261_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas261-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas261-attn-empty">' + escapeHtml(label('VAS_261_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas261-attn">' +
                '<div class="vas261-attn-title">' + escapeHtml(label('VAS_261_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_261_Period', 'Period');
            var invoicedLabel = label('VAS_261_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_261_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas261-band-ok' : 'vas261-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas261-sched-row">' +
                    '<span class="vas261-sched-left">' + left + '</span>' +
                    '<span class="vas261-sched-right">' +
                        '<span class="vas261-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas261-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas261-sched">' +
                '<div class="vas261-sched-title">' + escapeHtml(label('VAS_261_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas261-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas261-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_261_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_261_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas261-dtop2">' +
                    '<span class="vas261-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas261-dhead-main">' +
                        '<span class="vas261-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas261-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas261-dhead-right">' +
                        '<span class="vas261-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas261-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas261-stats">' +
                    statHtml('VAS_261_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_261_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_261_Type', 'Type', row.ContractType) +
                    statHtml('VAS_261_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_261_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_261_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_261_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_261_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
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
                showDetailMessage(label('VAS_261_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_261_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadSummary();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_261_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas261-modal-open'); }
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
            loadSummary();
        };

        this.refreshWidget = function () { loadSummary(); };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas261');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas261-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_261_NeedsAttentionWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_261_NeedsAttentionWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_261_NeedsAttentionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_261_NeedsAttentionWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_261_NeedsAttentionWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_261_NeedsAttentionWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
