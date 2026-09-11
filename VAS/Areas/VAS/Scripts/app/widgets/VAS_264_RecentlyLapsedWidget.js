/**
 * VAS_264 Recently Lapsed — Not Renewed Widget (Service Contracts dashboard)
 * Purpose - c4 x r3 recovery-list widget: lapsed contracts (Processed='Y',
 *           IsCancel='N', EndDate < CURRENT_DATE, no successor via a
 *           correlated NOT EXISTS on Ref_Contract_ID), most-recent lapse
 *           first - a win-back queue for the contracts manager. Header
 *           sub-line = "N expired without a successor · $X lost"; the header
 *           "All ->" link opens the full list modal. Each row shows a leading
 *           soft-danger "ban" icon tile, the customer (title), endText /
 *           renewal-type phrase / value lost (meta) and a Reactivate button.
 *           Row -> the shared contract detail modal (reusing
 *           VAS_241_ContractSearchWidget's GetContract / RunRenew /
 *           RunGenerateInvoice endpoints, the same cross-widget endpoint-reuse
 *           pattern VAS_120 set with VAS_126 and VAS_244/245/246/258/259
 *           already reused too), which surfaces a "Lapsed — not renewed"
 *           attention card (derived from GetContract's own LifecycleStatusCode
 *           = 'ENDED' - every row this widget ever opens is, by construction,
 *           already lapsed) and whose "Renew" / row "Reactivate" both run the
 *           real RenewContract process (VAS_241/RunRenew) - NOT the
 *           ModelLibrary "ReActiveContract" process the build spec names,
 *           which unfreezes an unrelated VAS_ContractMaster record set and
 *           never touches C_Contract (see the controller's file-header note);
 *           the pixel-source-of-truth mock's own openRenew() confirms
 *           RenewContract is correct here ("Renewal created · RenewContract"
 *           toast, even for a lapsed contract's "Reactivate"). "Open record"
 *           zooms to the Service Contract window. The widget never resizes on
 *           paging - fixed c4 x r3 footprint, outer overflow hidden, a full
 *           page shows all 7 rows with no clipping.
 * Design  - recently-lapsed.html (the parent Service Contracts dashboard mock,
 *           single-widget preview of lapsedWidget()) is the pixel-level
 *           source of truth; re-created verbatim below (tokens, soft-danger
 *           "ban" icon tile, header icon well/title/sub/"All ->" link, row
 *           structure, pager). The "All" list modal is a 3-column table
 *           (Contract / Value / Ends only - this widget's row DTO carries no
 *           Renewal/Status columns worth a header per the build spec).
 * Scope   - "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly this widget's lapsed
 *           population, fetched as a plain Contract_ID list
 *           (GetRecentlyLapsedContractIds) rather than reconstructed as a raw
 *           correlated-NOT-EXISTS where fragment client-side (this codebase's
 *           own VAS_140 widget shows that inlining relative date arithmetic
 *           into a raw where-clause breaks across Postgres/Oracle). When
 *           hosted (windowNo >= 0) this goes through widgetFirevalueChanged /
 *           ActionName - the same channel "Open record" already uses -
 *           resolved by NAME through the host window framework
 *           (VAS_244/245/246/258/259's own openInBrowser fix, 2026-09-08:
 *           VAS.ZoomUtil's hardcoded AD_Window_ID 1000248 turned out to
 *           resolve to a different window (Lead) on the real install).
 *
 * Backend - VAS_264_RecentlyLapsedWidget/GetRecentlyLapsedSummary     (GET -> LapsedCount, ValueLostBase, currency)
 *           VAS_264_RecentlyLapsedWidget/GetRecentlyLapsedContracts   (GET offset,limit -> rows + total)
 *           VAS_264_RecentlyLapsedWidget/GetRecentlyLapsedContractIds (GET -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract                 (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                    (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice          (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Recently Lapsed — Not Renewed         | VAS_264_Title
 *  2  | {n} expired without a successor       | VAS_264_SubCount
 *  3  | lost                                  | VAS_264_SubLost
 *  4  | All                                   | VAS_264_All
 *  5  | Couldn't load                         | VAS_264_LoadError
 *  6  | Recently Lapsed (list title)          | VAS_264_ListTitle
 *  7  | contracts                             | VAS_264_ContractsCount
 *  8  | Contract                              | VAS_264_ColContract
 *  9  | Value                                 | VAS_264_ColValue
 * 10  | Ends                                  | VAS_264_ColEnds
 * 11  | of                                    | VAS_264_Of
 * 12  | Previous page                         | VAS_264_PrevPage
 * 13  | Next page                             | VAS_264_NextPage
 * 14  | Nothing here right now.               | VAS_264_Empty
 * 15  | Unable to load contracts.             | VAS_264_UnableToLoad
 * 16  | Close                                 | VAS_264_Close
 * 17  | Open in browser                       | VAS_264_OpenInBrowser
 * 18  | Service Contract                      | VAS_264_ContractTitle
 * 19  | Open record                           | VAS_264_OpenRecord
 * 20  | rep                                   | VAS_264_Rep
 * 21  | cycles                                | VAS_264_Cycles
 * 22  | Contract cycle value                  | VAS_264_Value
 * 23  | Status                                | VAS_264_Status
 * 24  | Type                                  | VAS_264_Type
 * 25  | Renewal                               | VAS_264_Renewal
 * 26  | Ends                                  | VAS_264_Ends
 * 27  | in {n}d                               | VAS_264_EndsIn
 * 28  | Ended {n}d ago                        | VAS_264_EndedAgo
 * 29  | Notice days                           | VAS_264_NoticeDays
 * 30  | Billed amount                         | VAS_264_Billed
 * 31  | Unbilled amount                       | VAS_264_Unbilled
 * 32  | What needs attention                  | VAS_264_NeedsAttention
 * 33  | No open actions - contract is healthy.| VAS_264_NoOpenActions
 * 34  | Lapsed — not renewed                  | VAS_264_LapsedSignal
 * 35  | no successor contract                 | VAS_264_NoSuccessor
 * 36  | Billing overdue                       | VAS_264_BillingOverdue
 * 37  | Next period {n}d overdue              | VAS_264_NextPeriodOverdue
 * 38  | unbilled total                        | VAS_264_UnbilledTotal
 * 39  | open tickets for this customer        | VAS_264_OpenTickets
 * 40  | Billing schedule                      | VAS_264_BillingSchedule
 * 41  | Period                                | VAS_264_Period
 * 42  | Invoiced                              | VAS_264_Invoiced
 * 43  | Pending                               | VAS_264_SchedulePending
 * 44  | Generate invoice                      | VAS_264_GenerateInvoice
 * 45  | Renew                                 | VAS_264_Renew
 * 46  | Reactivate                            | VAS_264_Reactivate
 * 47  | Run RenewContract for this contract now? | VAS_264_ConfirmRenew
 * 48  | Generate invoices for the overdue billing periods now? | VAS_264_ConfirmGenerateInvoice
 * 49  | Working…                              | VAS_264_Working
 * 50  | The action failed.                    | VAS_264_ActionFailed
 * 51  | Couldn't load this contract.          | VAS_264_DetailLoadError
 * 52  | d (days-to-end suffix)                | VAS_264_DaysSuffix
 * 53  | ended {n}d ago                        | VAS_264_EndedAgoLower
 * 54  | auto (not renewed)                    | VAS_264_AutoNotRenewed
 * 55  | manual                                | VAS_264_Manual
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_264_RecentlyLapsedWidget/';

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

    // Detail-modal "Ends" stat (Sentence case, matching VAS_241/244/245/246/258/259).
    function endsText(days) {
        var d = Number(days || 0);
        if (d < 0) { return label('VAS_264_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_264_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    // Row meta "endText" (mock's own endText() - lower-case; every row here is
    // already ended, so only the "ended Nd ago" branch is ever hit).
    function endTextLower(days) {
        var d = Number(days || 0);
        return label('VAS_264_EndedAgoLower', 'ended {n}d ago').replace('{n}', formatCount(-d));
    }

    // Row meta renewal-type phrase (recently-lapsed.prompt.md §2 row spec) -
    // a composed phrase, not the decoded RenewalTypeLabel itself, driven by
    // the server-resolved code (never a client-invented A/M mapping).
    function renewalPhrase(code) {
        return String(code || '').toUpperCase() === 'A'
            ? label('VAS_264_AutoNotRenewed', 'auto (not renewed)')
            : label('VAS_264_Manual', 'manual');
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas264-band-ok'; }
        if (code === 'EXPIRING') { return 'vas264-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas264-band-danger'; }
        return 'vas264-band-info';
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
        if (name === 'ticket') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 9a3 3 0 0 1 0 6v2a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-2a3 3 0 0 1 0-6V7a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2z"></path><path d="M13 5v2M13 11v2M13 17v2"></path></svg>';
        }
        if (name === 'check') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';
        }
        if (name === 'ban') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="4.9" y1="4.9" x2="19.1" y2="19.1"></line></svg>';
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
    // .vas264-root's own clamp() font-size formulas) from the actual dashboard
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

    VAS.VAS_264_RecentlyLapsedWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas264-root">');
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
                '<div class="vas264-head">' +
                    '<div class="vas264-head-l">' +
                        '<span class="vas264-iconwell">' + icon('ban') + '</span>' +
                        '<span class="vas264-head-text">' +
                            '<span class="vas264-title">' + escapeHtml(label('VAS_264_Title', 'Recently Lapsed — Not Renewed')) + '</span>' +
                            '<span class="vas264-sub vas264-skel-sub">&nbsp;</span>' +
                        '</span>' +
                    '</div>' +
                    '<button type="button" class="vas264-alllink" data-open-list>' + escapeHtml(label('VAS_264_All', 'All')) + ' ' + icon('chev') + '</button>' +
                '</div>' +
                '<div class="vas264-body">' +
                    '<div class="vas264-list"></div>' +
                    '<div class="vas264-pager"></div>' +
                '</div>'
            );
            $body = $root.find('.vas264-body');
            $bodyList = $root.find('.vas264-list');
            $bodyPager = $root.find('.vas264-pager');
            $root.on('click', '[data-open-list]', function () { openList(); });
            $root.on('click', '.vas264-row', function () { openDetail(Number($(this).attr('data-cid'))); });
            $root.on('click', '[data-row-reactivate]', function (e) { e.stopPropagation(); runRowReactivate(Number($(this).attr('data-cid'))); });
            $root.on('click', '.vas264-pgbtn', function () { turnBodyPage($(this).attr('data-dir')); });
        }

        function subLine(count, valueText) {
            return formatCount(count) + ' ' + label('VAS_264_SubCount', 'expired without a successor') + ' · ' +
                valueText + ' ' + label('VAS_264_SubLost', 'lost');
        }

        function renderBodyError() {
            $root.find('.vas264-sub').removeClass('vas264-skel-sub').text(label('VAS_264_LoadError', "Couldn't load"));
            $bodyList.html('<div class="vas264-state">' + escapeHtml(label('VAS_264_UnableToLoad', 'Unable to load contracts.')) + '</div>');
            $bodyPager.empty();
        }

        function loadBody() {
            var seq = ++bodySeq;
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRecentlyLapsedContracts',
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
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRecentlyLapsedSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { return; }
                    var valueText = formatMoney(parsed.ValueLostBase, parsed.CurrencyIso, parsed.CurrencySymbol, parsed.CurrencyPrecision);
                    $root.find('.vas264-sub').removeClass('vas264-skel-sub').text(subLine(parsed.LapsedCount, valueText));
                },
                error: function () { /* sub-line just stays as last known value */ }
            });
        }

        function rowHtml(row) {
            var meta = [endTextLower(row.DaysToEnd), renewalPhrase(row.RenewalTypeCode), formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)].join(' · ');
            return '<div class="vas264-row" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas264-ic">' + icon('ban') + '</span>' +
                '<span class="vas264-row-main">' +
                    '<span class="vas264-row-name" title="' + escapeHtml(row.CustomerName || '') + '">' + escapeHtml(row.CustomerName || '') + '</span>' +
                    '<span class="vas264-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<button type="button" class="vas264-btn vas264-btn-ghost vas264-btn-sm" data-row-reactivate data-cid="' + Number(row.ContractId) + '">' + escapeHtml(label('VAS_264_Reactivate', 'Reactivate')) + '</button>' +
            '</div>';
        }

        function renderBody(rows) {
            if (!rows.length) {
                $bodyList.html('<div class="vas264-empty">' + icon('check') + '<span>' + escapeHtml(label('VAS_264_Empty', 'Nothing here right now.')) + '</span></div>');
                $bodyPager.empty();
                return;
            }

            $bodyList.html(rows.map(rowHtml).join(''));

            var pages = Math.max(1, Math.ceil(bodyTotal / BODY_PAGE));
            if (pages <= 1) { $bodyPager.empty(); return; }

            var current = Math.floor(bodyOffset / BODY_PAGE);
            var start = bodyOffset + 1, end = bodyOffset + rows.length;
            var of = label('VAS_264_Of', 'of');

            $bodyPager.html(
                '<span class="vas264-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(bodyTotal)) + '</span>' +
                '<span class="vas264-pgctl">' +
                    '<button type="button" class="vas264-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_264_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas264-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_264_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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

        // Row-level Reactivate runs standalone (confirm -> POST -> refresh the
        // row list) - it does NOT open the detail modal first. Routing it
        // through openDetail() used to show both the detail modal and the
        // confirm dialog stacked on top of each other at once (2026-09-09 fix).
        function runRowReactivate(contractId) {
            if (!contractId) { return; }
            showConfirm(label('VAS_264_ConfirmRenew', 'Run RenewContract for this contract now?'), function () {
                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'RunRenew',
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            if (window.VIS && VIS.ADialog) { VIS.ADialog.error('', false, (parsed && parsed.Message) || label('VAS_264_ActionFailed', 'The action failed.'), ''); }
                            return;
                        }
                        if (window.VIS && VIS.ADialog && parsed.Message) { VIS.ADialog.info(parsed.Message); }
                        loadBody();
                    },
                    error: function () {
                        if (window.VIS && VIS.ADialog) { VIS.ADialog.error('', false, label('VAS_264_ActionFailed', 'The action failed.'), ''); }
                    }
                });
            });
        }

        /* ---------- "All" lapsed-contracts list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas264-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas264-scrim" data-list-close></div>' +
                    '<section class="vas264-panel">' +
                        '<header class="vas264-phead">' +
                            '<h2 class="vas264-ptitle">' + escapeHtml(label('VAS_264_ListTitle', 'Recently Lapsed — Not Renewed')) + '</h2>' +
                            '<span class="vas264-pcount"></span>' +
                            '<button type="button" class="vas264-close" data-list-close aria-label="' + escapeHtml(label('VAS_264_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas264-colhead">' +
                            '<span>' + escapeHtml(label('VAS_264_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas264-col-r">' + escapeHtml(label('VAS_264_ColValue', 'Value')) + '</span>' +
                            '<span class="vas264-col-r">' + escapeHtml(label('VAS_264_ColEnds', 'Ends')) + '</span>' +
                        '</div>' +
                        '<div class="vas264-pbody"></div>' +
                        '<footer class="vas264-pfoot">' +
                            '<div class="vas264-pager2"></div>' +
                            '<div class="vas264-pfoot-actions">' +
                                '<button type="button" class="vas264-btn vas264-btn-ghost" data-list-close>' + escapeHtml(label('VAS_264_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas264-btn vas264-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_264_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas264-pbody');
            $listPager = $list.find('.vas264-pager2');
            $listCount = $list.find('.vas264-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas264-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas264-pgbtn', function () { turnListPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas264', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function openList() {
            listOffset = 0;
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas264-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas264-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas264-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRecentlyLapsedContracts',
                type: 'GET', dataType: 'json', cache: false,
                data: { offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas264-state">' + escapeHtml(label('VAS_264_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_264_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas264-state">' + escapeHtml(label('VAS_264_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            return '<div class="vas264-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas264-mtc-main">' +
                    '<span class="vas264-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas264-mtc-text">' +
                        '<span class="vas264-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas264-row-meta">' + escapeHtml([row.DocumentNo, row.ProductName].filter(function (p) { return p; }).join(' · ')) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas264-col-r"><b>' + escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</b></span>' +
                '<span class="vas264-col-r">' + escapeHtml(endTextLower(row.DaysToEnd)) + '</span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas264-state">' + escapeHtml(label('VAS_264_Empty', 'Nothing here right now.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_264_Of', 'of');

            $listPager.html(
                '<span class="vas264-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas264-pgctl">' +
                    '<button type="button" class="vas264-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_264_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas264-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_264_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
        // to exactly this widget's lapsed population, fetched as a plain
        // Contract_ID list (GetRecentlyLapsedContractIds) rather than
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRecentlyLapsedContractIds',
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
                '<div class="vas264-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas264-scrim" data-detail-close></div>' +
                    '<section class="vas264-panel">' +
                        '<header class="vas264-mhead">' +
                            '<h2 class="vas264-mtitle">' + escapeHtml(label('VAS_264_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas264-mhead-meta"></span>' +
                            '<button type="button" class="vas264-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_264_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas264-mbody"></div>' +
                        '<div class="vas264-mmsg" role="status"></div>' +
                        '<footer class="vas264-mfoot">' +
                            '<button type="button" class="vas264-btn vas264-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_264_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas264-btn vas264-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_264_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas264-btn vas264-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_264_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas264-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_264_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_264_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas264-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas264-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas264-confirm-box">' +
                        '<p class="vas264-confirm-msg"></p>' +
                        '<div class="vas264-confirm-actions">' +
                            '<button type="button" class="vas264-btn vas264-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_264_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas264-btn vas264-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_264_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas264-confirm-msg');
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
            return '<div class="vas264-stat">' +
                '<span class="vas264-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas264-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
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
            $detailBody = $detail.find('.vas264-mbody');
            $detail.find('.vas264-mhead-meta').empty();
            $detailBody.html('<div class="vas264-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas264-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas264-state">' + escapeHtml(label('VAS_264_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas264-state">' + escapeHtml(label('VAS_264_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            // Every row this widget ever opens is, by construction, already
            // lapsed (GetRecentlyLapsedContracts' own WHERE predicate); ENDED
            // from GetContract's independently-computed LifecycleStatusCode
            // (DaysToEnd < 0) is a safe, already-established proxy signal here.
            if (String(row.LifecycleStatusCode || '').toUpperCase() === 'ENDED') {
                cards += '<div class="vas264-attn-card">' +
                    '<span class="vas264-attn-ic vas264-attn-ic-danger">' + icon('ban') + '</span>' +
                    '<span class="vas264-attn-main">' +
                        '<span class="vas264-attn-title2">' + escapeHtml(label('VAS_264_LapsedSignal', 'Lapsed — not renewed')) + '</span>' +
                        '<span class="vas264-attn-desc">' + escapeHtml(endsText(row.DaysToEnd) + ' · ' + label('VAS_264_NoSuccessor', 'no successor contract')) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_264_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_264_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas264-attn-card">' +
                    '<span class="vas264-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas264-attn-main">' +
                        '<span class="vas264-attn-title2">' + escapeHtml(label('VAS_264_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas264-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas264-attn-empty">' + escapeHtml(label('VAS_264_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas264-attn">' +
                '<div class="vas264-attn-title">' + escapeHtml(label('VAS_264_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_264_Period', 'Period');
            var invoicedLabel = label('VAS_264_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_264_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas264-band-ok' : 'vas264-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas264-sched-row">' +
                    '<span class="vas264-sched-left">' + left + '</span>' +
                    '<span class="vas264-sched-right">' +
                        '<span class="vas264-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas264-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas264-sched">' +
                '<div class="vas264-sched-title">' + escapeHtml(label('VAS_264_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas264-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas264-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_264_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_264_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas264-dtop2">' +
                    '<span class="vas264-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas264-dhead-main">' +
                        '<span class="vas264-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas264-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas264-dhead-right">' +
                        '<span class="vas264-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas264-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas264-stats">' +
                    statHtml('VAS_264_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_264_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_264_Type', 'Type', row.ContractType) +
                    statHtml('VAS_264_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_264_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_264_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_264_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_264_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
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
                showDetailMessage(label('VAS_264_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_264_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadBody();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_264_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas264-modal-open'); }
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
            $(document).off('keydown.MPCvas264');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas264-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_264_RecentlyLapsedWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_264_RecentlyLapsedWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_264_RecentlyLapsedWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_264_RecentlyLapsedWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_264_RecentlyLapsedWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_264_RecentlyLapsedWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
