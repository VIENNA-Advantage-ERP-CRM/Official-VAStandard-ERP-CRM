/**
 * VAS_260 Contracts By Status Widget (Service Contracts dashboard)
 * Purpose - c3 x r2 read-only distribution widget: the portfolio split across
 *           five DERIVED lifecycle bands - Active / Expiring (<=90) / Draft /
 *           Expired / Cancelled - as horizontal bars (colour swatch + label
 *           left, count - pct% right, a proportional fill below). Header sub-
 *           line = "N contracts - DocStatus + lifecycle". Each segment is
 *           clickable and opens that band's own contract list modal (paged
 *           @7, using the band's own CASE predicate as the WHERE). Row -> the
 *           shared contract detail modal (reusing VAS_241_ContractSearchWidget's
 *           GetContract / RunRenew / RunGenerateInvoice endpoints, the same
 *           cross-widget endpoint-reuse pattern VAS_120 set with VAS_126 and
 *           VAS_244/245/246/258/259/261-267 already reused too), whose
 *           "Renew"/"Generate invoice" run the real processes and "Open
 *           record" zooms to the Service Contract window (2026-09-09: the
 *           detail modal's Renew/Generate invoice buttons were originally
 *           omitted here - this widget's own distribution/drill body stays
 *           read-only, but the shared detail modal now matches every sibling
 *           widget's own footer). The widget never resizes - fixed c3 x r2
 *           footprint, five bars vertically centred.
 * Design  - contracts-by-status.html (the parent Service Contracts dashboard
 *           mock, single-widget preview of statusWidget()) is the pixel-level
 *           source of truth; re-created verbatim below (tokens, segment/bar
 *           structure, header icon well/title/sub, no "All ->" link - drill
 *           is per-segment).
 * Scope   - "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly the clicked band's
 *           population, fetched as a plain Contract_ID list
 *           (GetContractsByStatusIds) rather than reconstructed as a raw
 *           where fragment client-side (this codebase's own VAS_140 widget
 *           shows that inlining relative date arithmetic into a raw where-
 *           clause breaks across Postgres/Oracle). When hosted (windowNo >=
 *           0) this goes through widgetFirevalueChanged / ActionName - the
 *           same channel "Open record" already uses - resolved by NAME
 *           through the host window framework (VAS_244/245/246/258/259's own
 *           openInBrowser fix, 2026-09-08: VAS.ZoomUtil's hardcoded
 *           AD_Window_ID 1000248 turned out to resolve to a different window
 *           (Lead) on the real install).
 *
 * Backend - VAS_260_ContractsByStatusWidget/GetContractsByStatusSummary (GET -> five band counts + Total)
 *           VAS_260_ContractsByStatusWidget/GetContractsByStatusDrill    (GET band,offset,limit -> rows + total)
 *           VAS_260_ContractsByStatusWidget/GetContractsByStatusIds      (GET band -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract                     (GET id -> contract detail + windowId) [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Contracts by Status                   | VAS_260_Title
 *  2  | {n} contracts - DocStatus + lifecycle | VAS_260_SubLine
 *  3  | Active                                | VAS_260_BandActive
 *  4  | Expiring                              | VAS_260_BandExpiring
 *  5  | Draft                                 | VAS_260_BandDraft
 *  6  | Expired                               | VAS_260_BandExpired
 *  7  | Cancelled                             | VAS_260_BandCancelled
 *  8  | No contracts.                         | VAS_260_NoContracts
 *  9  | Couldn't load                         | VAS_260_LoadError
 * 10  | Status ·                              | VAS_260_ListTitlePrefix
 * 11  | contracts                             | VAS_260_ContractsCount
 * 12  | Contract                              | VAS_260_ColContract
 * 13  | Value                                 | VAS_260_ColValue
 * 14  | Ends                                  | VAS_260_ColEnds
 * 15  | Renewal                               | VAS_260_ColRenewal
 * 16  | Status                                | VAS_260_ColStatus
 * 17  | of                                    | VAS_260_Of
 * 18  | Previous page                         | VAS_260_PrevPage
 * 19  | Next page                             | VAS_260_NextPage
 * 20  | No contracts.                         | VAS_260_Empty
 * 21  | Unable to load contracts.             | VAS_260_UnableToLoad
 * 22  | Close                                 | VAS_260_Close
 * 23  | Open in browser                       | VAS_260_OpenInBrowser
 * 24  | Service Contract                      | VAS_260_ContractTitle
 * 25  | Open record                           | VAS_260_OpenRecord
 * 26  | rep                                   | VAS_260_Rep
 * 27  | cycles                                | VAS_260_Cycles
 * 28  | Contract cycle value                  | VAS_260_Value
 * 29  | Status                                | VAS_260_Status
 * 30  | Type                                  | VAS_260_Type
 * 31  | Renewal                               | VAS_260_Renewal
 * 32  | Ends                                  | VAS_260_Ends
 * 33  | in {n}d                               | VAS_260_EndsIn
 * 34  | Ended {n}d ago                        | VAS_260_EndedAgo
 * 35  | Notice days                           | VAS_260_NoticeDays
 * 36  | Billed amount                         | VAS_260_Billed
 * 37  | Unbilled amount                       | VAS_260_Unbilled
 * 38  | What needs attention                  | VAS_260_NeedsAttention
 * 39  | No open actions - contract is healthy.| VAS_260_NoOpenActions
 * 40  | Renewal notice overdue                | VAS_260_RenewalNoticeOverdue
 * 41  | Renewal notice due today              | VAS_260_RenewalNoticeDueToday
 * 42  | Manual renewal                        | VAS_260_ManualRenewal
 * 43  | notice {n}d overdue                   | VAS_260_NoticeOverdueBy
 * 44  | ends                                  | VAS_260_EndsPrefix
 * 45  | Billing overdue                       | VAS_260_BillingOverdue
 * 46  | Next period {n}d overdue              | VAS_260_NextPeriodOverdue
 * 47  | unbilled total                        | VAS_260_UnbilledTotal
 * 48  | open tickets for this customer        | VAS_260_OpenTickets
 * 49  | Billing schedule                      | VAS_260_BillingSchedule
 * 50  | Period                                | VAS_260_Period
 * 51  | Invoiced                              | VAS_260_Invoiced
 * 52  | Pending                               | VAS_260_SchedulePending
 * 53  | Generate invoice                      | VAS_260_GenerateInvoice
 * 54  | Renew                                 | VAS_260_Renew
 * 55  | Run RenewContract for this contract now? | VAS_260_ConfirmRenew
 * 56  | Generate invoices for the overdue billing periods now? | VAS_260_ConfirmGenerateInvoice
 * 57  | Working…                              | VAS_260_Working
 * 58  | The action failed.                    | VAS_260_ActionFailed
 * 59  | Couldn't load this contract.          | VAS_260_DetailLoadError
 * 60  | d (days-to-end suffix)                | VAS_260_DaysSuffix
 * 60a | d ago (past days-to-end suffix)       | VAS_260_DaysAgoSuffix
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_260_ContractsByStatusWidget/';

    var LIST_PAGE = 7;

    // Band order, colour and message key - fixed (contracts-by-status.queries.md
    // D.1's five mutually-exclusive bands), never reordered by count.
    var BANDS = [
        { key: 'Active', color: '#20A464', labelKey: 'VAS_260_BandActive', fallback: 'Active' },
        { key: 'Expiring', color: '#D78B10', labelKey: 'VAS_260_BandExpiring', fallback: 'Expiring' },
        { key: 'Draft', color: '#1F83FF', labelKey: 'VAS_260_BandDraft', fallback: 'Draft' },
        { key: 'Expired', color: '#D8434A', labelKey: 'VAS_260_BandExpired', fallback: 'Expired' },
        { key: 'Cancelled', color: '#8A99A8', labelKey: 'VAS_260_BandCancelled', fallback: 'Cancelled' }
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
        if (d < 0) { return label('VAS_260_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_260_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    function isManualRenewal(code) {
        var c = String(code || '').toUpperCase();
        return c === 'M' || c === 'MNL';
    }

    function renewalClass(code) {
        var c = String(code || '').toUpperCase();
        if (c === 'A' || c === 'ATC') { return 'vas260-renewal-auto'; }
        if (c === 'M' || c === 'MNL') { return 'vas260-renewal-manual'; }
        return 'vas260-renewal-neutral';
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas260-band-ok'; }
        if (code === 'EXPIRING') { return 'vas260-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas260-band-danger'; }
        return 'vas260-band-info';
    }

    // Drill row Status column shows the widget's OWN clicked band (decoded
    // DocStatus alone can't distinguish Expiring from Active - both are 'CO').
    function bandTagClass(bandKey) {
        var k = String(bandKey || '').toUpperCase();
        if (k === 'ACTIVE') { return 'vas260-band-ok'; }
        if (k === 'EXPIRING') { return 'vas260-band-warn'; }
        if (k === 'EXPIRED' || k === 'CANCELLED') { return 'vas260-band-danger'; }
        return 'vas260-band-info';
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
        if (name === 'layers') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>';
        }
        return '';
    }

    // Populates --dash-inline-size (a shared, dashboard-wide CSS var read by
    // .vas260-root's own clamp() font-size formula) from the actual dashboard
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

    VAS.VAS_260_ContractsByStatusWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas260-root">');
        var $sub, $segs;

        var currentBand = '';

        var $list, $listBody, $listPager, $listCount;
        var listOffset = 0, listTotal = 0, listSeq = 0;

        var $detail, $detailBody, $detailMsg;
        var detailBusy = false;

        var $confirm, $confirmMsg;
        var confirmCallback = null;

        var zoomWindowId = ZOOM_WINDOW_ID;

        /* ---------- Widget header + five distribution bars ---------- */

        function createWidget() {
            $root.html(
                '<div class="vas260-head">' +
                    '<span class="vas260-iconwell">' + icon('layers') + '</span>' +
                    '<span class="vas260-head-text">' +
                        '<span class="vas260-title">' + escapeHtml(label('VAS_260_Title', 'Contracts by Status')) + '</span>' +
                        '<span class="vas260-sub vas260-skel-sub">&nbsp;</span>' +
                    '</span>' +
                '</div>' +
                '<div class="vas260-body"><div class="vas260-segs"></div></div>'
            );
            $sub = $root.find('.vas260-sub');
            $segs = $root.find('.vas260-segs');
            $root.on('click', '[data-open-band]', function () { openList($(this).attr('data-open-band')); });
        }

        function renderError() {
            $sub.removeClass('vas260-skel-sub').text(label('VAS_260_LoadError', "Couldn't load"));
            $segs.html('<div class="vas260-state">' + escapeHtml(label('VAS_260_UnableToLoad', 'Unable to load contracts.')) + '</div>');
        }

        function segHtml(band, count, pct, widthPct) {
            return '<div class="vas260-seg">' +
                '<div class="vas260-seg-top">' +
                    '<span class="vas260-seg-l" data-open-band="' + band.key + '">' +
                        '<span class="vas260-sw" style="background:' + band.color + '"></span>' +
                        escapeHtml(label(band.labelKey, band.fallback)) +
                    '</span>' +
                    '<span class="vas260-seg-r"><span class="vas260-seg-v">' + formatCount(count) + '</span> · ' + pct + '%</span>' +
                '</div>' +
                '<div class="vas260-bar"><div style="width:' + widthPct + '%;background:' + band.color + '"></div></div>' +
            '</div>';
        }

        function renderSummary(data) {
            var total = Number(data.Total || 0);
            $sub.removeClass('vas260-skel-sub').text(formatCount(total) + ' ' + label('VAS_260_SubLine', 'contracts · DocStatus + lifecycle'));

            if (!total) {
                $segs.html('<div class="vas260-state">' + escapeHtml(label('VAS_260_NoContracts', 'No contracts.')) + '</div>');
                return;
            }

            var counts = {
                Active: Number(data.ActiveCount || 0),
                Expiring: Number(data.ExpiringCount || 0),
                Draft: Number(data.DraftCount || 0),
                Expired: Number(data.ExpiredCount || 0),
                Cancelled: Number(data.CancelledCount || 0)
            };
            var max = Math.max(counts.Active, counts.Expiring, counts.Draft, counts.Expired, counts.Cancelled, 1);

            var html = BANDS.map(function (band) {
                var count = counts[band.key] || 0;
                var pct = Math.round((count / total) * 100);
                var widthPct = Math.round((count / max) * 100);
                return segHtml(band, count, pct, widthPct);
            }).join('');

            $segs.html(html);
        }

        function loadSummary() {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetContractsByStatusSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { renderError(); return; }
                    renderSummary(parsed);
                },
                error: function () { renderError(); }
            });
        }

        /* ---------- Per-band drill list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas260-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas260-scrim" data-list-close></div>' +
                    '<section class="vas260-panel">' +
                        '<header class="vas260-phead">' +
                            '<h2 class="vas260-ptitle"></h2>' +
                            '<span class="vas260-pcount"></span>' +
                            '<button type="button" class="vas260-close" data-list-close aria-label="' + escapeHtml(label('VAS_260_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas260-colhead">' +
                            '<span>' + escapeHtml(label('VAS_260_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas260-col-r">' + escapeHtml(label('VAS_260_ColValue', 'Value')) + '</span>' +
                            '<span class="vas260-col-r">' + escapeHtml(label('VAS_260_ColEnds', 'Ends')) + '</span>' +
                            '<span>' + escapeHtml(label('VAS_260_ColRenewal', 'Renewal')) + '</span>' +
                            '<span class="vas260-col-r">' + escapeHtml(label('VAS_260_ColStatus', 'Status')) + '</span>' +
                        '</div>' +
                        '<div class="vas260-pbody"></div>' +
                        '<footer class="vas260-pfoot">' +
                            '<div class="vas260-pager"></div>' +
                            '<div class="vas260-pfoot-actions">' +
                                '<button type="button" class="vas260-btn vas260-btn-ghost" data-list-close>' + escapeHtml(label('VAS_260_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas260-btn vas260-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_260_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas260-pbody');
            $listPager = $list.find('.vas260-pager');
            $listCount = $list.find('.vas260-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas260-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas260-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas260', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function bandTitle(band) {
            var found = BANDS.filter(function (b) { return b.key === band; })[0];
            var name = found ? label(found.labelKey, found.fallback) : band;
            return label('VAS_260_ListTitlePrefix', 'Status ·') + ' ' + name;
        }

        function openList(band) {
            currentBand = band;
            listOffset = 0;
            $list.find('.vas260-ptitle').text(bandTitle(band));
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas260-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas260-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas260-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetContractsByStatusDrill',
                type: 'GET', dataType: 'json', cache: false,
                data: { band: currentBand, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas260-state">' + escapeHtml(label('VAS_260_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_260_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas260-state">' + escapeHtml(label('VAS_260_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            return '<div class="vas260-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas260-mtc-main">' +
                    '<span class="vas260-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas260-mtc-text">' +
                        '<span class="vas260-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas260-row-meta">' + escapeHtml([row.DocumentNo, row.ProductName].filter(function (p) { return p; }).join(' · ')) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas260-col-r"><b>' + escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</b></span>' +
                '<span class="vas260-col-r">' + escapeHtml(Number(row.DaysToEnd || 0) < 0 ? formatCount(-row.DaysToEnd) + label('VAS_260_DaysAgoSuffix', 'd ago') : formatCount(row.DaysToEnd) + label('VAS_260_DaysSuffix', 'd')) + '</span>' +
                '<span><span class="vas260-renewal ' + renewalClass(row.RenewalTypeCode) + '"><span class="vas260-dot"></span>' + escapeHtml(row.RenewalType || '') + '</span></span>' +
                '<span class="vas260-col-r"><span class="vas260-status ' + bandTagClass(currentBand) + '">' + escapeHtml(row.DocStatus || '') + '</span></span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas260-state">' + escapeHtml(label('VAS_260_Empty', 'No contracts.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_260_Of', 'of');

            $listPager.html(
                '<span class="vas260-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas260-pgctl">' +
                    '<button type="button" class="vas260-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_260_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas260-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_260_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
        // to exactly the clicked band's population, fetched as a plain Contract_ID
        // list (GetContractsByStatusIds) rather than reconstructed as a raw where
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetContractsByStatusIds',
                        type: 'GET', dataType: 'json', cache: false,
                        data: { band: currentBand },
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
                '<div class="vas260-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas260-scrim" data-detail-close></div>' +
                    '<section class="vas260-panel">' +
                        '<header class="vas260-mhead">' +
                            '<h2 class="vas260-mtitle">' + escapeHtml(label('VAS_260_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas260-mhead-meta"></span>' +
                            '<button type="button" class="vas260-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_260_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas260-mbody"></div>' +
                        '<div class="vas260-mmsg" role="status"></div>' +
                        '<footer class="vas260-mfoot">' +
                            '<button type="button" class="vas260-btn vas260-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_260_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas260-btn vas260-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_260_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas260-btn vas260-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_260_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas260-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_260_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_260_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas260-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas260-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas260-confirm-box">' +
                        '<p class="vas260-confirm-msg"></p>' +
                        '<div class="vas260-confirm-actions">' +
                            '<button type="button" class="vas260-btn vas260-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_260_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas260-btn vas260-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_260_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas260-confirm-msg');
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

        function showDetailMessage(message, isError) {
            if (!message) { $detailMsg.removeClass('is-error is-open').empty(); return; }
            $detailMsg.text(message).toggleClass('is-error', !!isError).addClass('is-open');
        }

        function setDetailBusy(busy) {
            detailBusy = busy;
            $detail.find('[data-detail-renew], [data-detail-invoice]').prop('disabled', busy);
        }

        function runContractAction(endpoint, confirmMessage) {
            if (detailBusy) { return; }
            var contractId = Number($detail.attr('data-cid'));
            if (!contractId) { return; }

            showConfirm(confirmMessage, function () {
                setDetailBusy(true);
                showDetailMessage(label('VAS_260_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_260_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadSummary();
                        if (currentBand) { loadList(); }
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_260_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function statHtml(labelKey, fallback, value) {
            return '<div class="vas260-stat">' +
                '<span class="vas260-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas260-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
            '</div>';
        }

        function openDetail(contractId) {
            if (!contractId) { return; }
            $detail.attr('data-cid', contractId);
            $detailBody = $detail.find('.vas260-mbody');
            $detail.find('.vas260-mhead-meta').empty();
            $detailBody.html('<div class="vas260-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas260-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas260-state">' + escapeHtml(label('VAS_260_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas260-state">' + escapeHtml(label('VAS_260_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            if (isManualRenewal(row.RenewalTypeCode) && !row.HasSuccessor && row.NoticeSlackDays != null && row.NoticeSlackDays <= 0) {
                var noticeTitle = row.NoticeSlackDays < 0
                    ? label('VAS_260_RenewalNoticeOverdue', 'Renewal notice overdue')
                    : label('VAS_260_RenewalNoticeDueToday', 'Renewal notice due today');
                var noticeDescParts = [label('VAS_260_ManualRenewal', 'Manual renewal')];
                if (row.NoticeSlackDays < 0) {
                    noticeDescParts.push(label('VAS_260_NoticeOverdueBy', 'notice {n}d overdue').replace('{n}', formatCount(-row.NoticeSlackDays)));
                }
                noticeDescParts.push(label('VAS_260_EndsPrefix', 'ends') + ' ' + endsText(row.DaysToEnd));
                cards += '<div class="vas260-attn-card">' +
                    '<span class="vas260-attn-ic vas260-attn-ic-danger">' + icon('warning') + '</span>' +
                    '<span class="vas260-attn-main">' +
                        '<span class="vas260-attn-title2">' + escapeHtml(noticeTitle) + '</span>' +
                        '<span class="vas260-attn-desc">' + escapeHtml(noticeDescParts.join(' · ')) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_260_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_260_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas260-attn-card">' +
                    '<span class="vas260-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas260-attn-main">' +
                        '<span class="vas260-attn-title2">' + escapeHtml(label('VAS_260_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas260-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas260-attn-empty">' + escapeHtml(label('VAS_260_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas260-attn">' +
                '<div class="vas260-attn-title">' + escapeHtml(label('VAS_260_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_260_Period', 'Period');
            var invoicedLabel = label('VAS_260_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_260_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas260-band-ok' : 'vas260-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas260-sched-row">' +
                    '<span class="vas260-sched-left">' + left + '</span>' +
                    '<span class="vas260-sched-right">' +
                        '<span class="vas260-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas260-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas260-sched">' +
                '<div class="vas260-sched-title">' + escapeHtml(label('VAS_260_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas260-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas260-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_260_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_260_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas260-dtop2">' +
                    '<span class="vas260-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas260-dhead-main">' +
                        '<span class="vas260-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas260-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas260-dhead-right">' +
                        '<span class="vas260-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas260-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas260-stats">' +
                    statHtml('VAS_260_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_260_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_260_Type', 'Type', row.ContractType) +
                    statHtml('VAS_260_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_260_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_260_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_260_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_260_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                '</div>' +
                attentionCards(row) +
                scheduleHtml(row);

            $detailBody.html(html);
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas260-modal-open'); }
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
            $(document).off('keydown.MPCvas260');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas260-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_260_ContractsByStatusWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_260_ContractsByStatusWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_260_ContractsByStatusWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_260_ContractsByStatusWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_260_ContractsByStatusWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_260_ContractsByStatusWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
