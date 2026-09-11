/**
 * VAS_262 Value By Contract Type Widget (Service Contracts dashboard)
 * Purpose - c3 x r2 read-only breakdown widget: one ranked horizontal bar per
 *           C_Contract.ContractType (Support / Supply / Standard Reseller /
 *           Product Eligibility / ...), each showing the summed live
 *           portfolio value (base currency) with a trailing "- N" contract
 *           count. Bars are value-ranked (largest first); a type with no live
 *           contracts renders no bar. Header sub-line = total value across
 *           all types. Each bar is clickable -> the "Type - <label>" contract
 *           list modal (paged @7, value-descending). Row -> the shared
 *           contract detail modal (reusing VAS_241_ContractSearchWidget's
 *           GetContract / RunRenew / RunGenerateInvoice endpoints, the same
 *           cross-widget endpoint-reuse pattern VAS_120 set with VAS_126 and
 *           VAS_244/245/246/258/259/260/261 already reused too), whose "Renew"
 *           runs the real RenewContract process, "Generate invoice" runs
 *           CreateContractInvoice, and "Open record" zooms to the Service
 *           Contract window. The breakdown itself is read-only, no pager -
 *           the bar set is small and fills a fixed c3 x r2 footprint,
 *           vertically centred; the widget never resizes.
 * Design  - value-by-type.html (the parent Service Contracts dashboard mock,
 *           single-widget preview of valueByTypeWidget()) is the pixel-level
 *           source of truth; re-created verbatim below (tokens, seg/bar
 *           structure, header icon well/title/sub, no "All ->" link - the
 *           whole widget is the breakdown, bars are the drill).
 * Scope   - "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly the clicked type's
 *           population, fetched as a plain Contract_ID list
 *           (GetValueByTypeIds) rather than reconstructed as a raw where
 *           fragment client-side (this codebase's own VAS_140 widget shows
 *           that inlining relative date arithmetic into a raw where-clause
 *           breaks across Postgres/Oracle). When hosted (windowNo >= 0) this
 *           goes through widgetFirevalueChanged / ActionName - the same
 *           channel "Open record" already uses - resolved by NAME through the
 *           host window framework (VAS_244/245/246/258/259/260/261's own
 *           openInBrowser fix, 2026-09-08: VAS.ZoomUtil's hardcoded
 *           AD_Window_ID 1000248 turned out to resolve to a different window
 *           (Lead) on the real install). "Live" reads Processed instead of
 *           DocStatus, the same fix already applied to VAS_243/244/245/246/
 *           258/259/260/261 (see the controller's own header note). The
 *           drill list's Status column is a client-side computed lifecycle
 *           read-out from DaysToEnd (Active beyond 90 days, Expiring within
 *           90 - every row here is already known live/completed by
 *           definition, so no further DocStatus decode is needed to
 *           distinguish it, matching VAS_258's own rowLifecycleStatus).
 *
 * Backend - VAS_262_ValueByTypeWidget/GetValueByTypeBreakdown (GET -> ranked Types[] + TotalValueBase + currency)
 *           VAS_262_ValueByTypeWidget/GetValueByTypeDrill      (GET typeCode,offset,limit -> rows + total)
 *           VAS_262_ValueByTypeWidget/GetValueByTypeIds        (GET typeCode -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract            (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice       (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Value by Active Contract Type         | VAS_262_Title
 *  2  | across ContractType                   | VAS_262_SubSuffix
 *  3  | No contracts.                         | VAS_262_NoTypes
 *  4  | Nothing here right now.               | VAS_262_Nothing
 *  5  | Couldn't load                         | VAS_262_LoadError
 *  6  | Type ·                                | VAS_262_ListTitlePrefix
 *  7  | contracts                             | VAS_262_ContractsCount
 *  8  | Contract                              | VAS_262_ColContract
 *  9  | Value                                 | VAS_262_ColValue
 * 10  | Ends                                  | VAS_262_ColEnds
 * 11  | Renewal                               | VAS_262_ColRenewal
 * 12  | Status                                | VAS_262_ColStatus
 * 13  | of                                    | VAS_262_Of
 * 14  | Previous page                         | VAS_262_PrevPage
 * 15  | Next page                             | VAS_262_NextPage
 * 16  | No contracts.                         | VAS_262_Empty
 * 17  | Unable to load contracts.             | VAS_262_UnableToLoad
 * 18  | Close                                 | VAS_262_Close
 * 19  | Open in browser                       | VAS_262_OpenInBrowser
 * 20  | Service Contract                      | VAS_262_ContractTitle
 * 21  | Open record                           | VAS_262_OpenRecord
 * 22  | rep                                   | VAS_262_Rep
 * 23  | cycles                                | VAS_262_Cycles
 * 24  | Contract cycle value                  | VAS_262_Value
 * 25  | Status                                | VAS_262_Status
 * 26  | Type                                  | VAS_262_Type
 * 27  | Renewal                               | VAS_262_Renewal
 * 28  | Ends                                  | VAS_262_Ends
 * 29  | in {n}d                               | VAS_262_EndsIn
 * 30  | Ended {n}d ago                        | VAS_262_EndedAgo
 * 31  | Notice days                           | VAS_262_NoticeDays
 * 32  | Billed amount                         | VAS_262_Billed
 * 33  | Unbilled amount                       | VAS_262_Unbilled
 * 34  | What needs attention                  | VAS_262_NeedsAttention
 * 35  | No open actions - contract is healthy.| VAS_262_NoOpenActions
 * 36  | Renewal notice overdue                | VAS_262_RenewalNoticeOverdue
 * 37  | Renewal notice due today              | VAS_262_RenewalNoticeDueToday
 * 38  | Manual renewal                        | VAS_262_ManualRenewal
 * 39  | notice {n}d overdue                   | VAS_262_NoticeOverdueBy
 * 40  | ends                                  | VAS_262_EndsPrefix
 * 41  | Billing overdue                       | VAS_262_BillingOverdue
 * 42  | Next period {n}d overdue              | VAS_262_NextPeriodOverdue
 * 43  | unbilled total                        | VAS_262_UnbilledTotal
 * 44  | open tickets for this customer        | VAS_262_OpenTickets
 * 45  | Billing schedule                      | VAS_262_BillingSchedule
 * 46  | Period                                | VAS_262_Period
 * 47  | Invoiced                              | VAS_262_Invoiced
 * 48  | Pending                               | VAS_262_SchedulePending
 * 49  | Generate invoice                      | VAS_262_GenerateInvoice
 * 50  | Renew                                 | VAS_262_Renew
 * 51  | Run RenewContract for this contract now? | VAS_262_ConfirmRenew
 * 52  | Generate invoices for the overdue billing periods now? | VAS_262_ConfirmGenerateInvoice
 * 53  | Working…                              | VAS_262_Working
 * 54  | The action failed.                    | VAS_262_ActionFailed
 * 55  | Couldn't load this contract.          | VAS_262_DetailLoadError
 * 56  | d (days-to-end suffix)                | VAS_262_DaysSuffix
 * 56a | d ago (past days-to-end suffix)       | VAS_262_DaysAgoSuffix
 * 57  | Active                                | VAS_262_StatusActive
 * 58  | Expiring                              | VAS_262_StatusExpiring
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_262_ValueByTypeWidget/';

    var LIST_PAGE = 7;

    // Stable per-type colour (value-by-type.prompt.md §4) - falls back to a
    // neutral tone for a type not in this fixed palette (the ref-list set is
    // install-configurable, so new/unlisted types must still render).
    var TYPE_COLORS = {
        'Support': '#0083DA',
        'Supply': '#5F4AA6',
        'Standard Reseller': '#20A464',
        'Product Eligibility': '#D78B10'
    };
    var FALLBACK_COLORS = ['#0083DA', '#5F4AA6', '#20A464', '#D78B10', '#0B6B45', '#A33F3F'];
    function typeColor(label, index) {
        if (TYPE_COLORS[label]) { return TYPE_COLORS[label]; }
        return FALLBACK_COLORS[index % FALLBACK_COLORS.length];
    }

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
        if (d < 0) { return label('VAS_262_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_262_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    function isManualRenewal(code) {
        var c = String(code || '').toUpperCase();
        return c === 'M' || c === 'MNL';
    }

    function renewalClass(code) {
        var c = String(code || '').toUpperCase();
        if (c === 'A' || c === 'ATC') { return 'vas262-renewal-auto'; }
        if (c === 'M' || c === 'MNL') { return 'vas262-renewal-manual'; }
        return 'vas262-renewal-neutral';
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas262-band-ok'; }
        if (code === 'EXPIRING') { return 'vas262-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas262-band-danger'; }
        return 'vas262-band-info';
    }

    // Every drill row here is already known live (Processed='Y', not cancelled,
    // not yet ended) - the Status column is a client-side lifecycle read-out
    // from DaysToEnd alone (Active beyond 90 days, Expiring within), matching
    // VAS_258's own rowLifecycleStatus/rowLifecycleClass.
    function rowLifecycleStatus(daysToEnd) {
        var d = Number(daysToEnd || 0);
        if (d <= 90) { return label('VAS_262_StatusExpiring', 'Expiring'); }
        return label('VAS_262_StatusActive', 'Active');
    }
    function rowLifecycleClass(daysToEnd) {
        var d = Number(daysToEnd || 0);
        if (d <= 90) { return 'vas262-band-warn'; }
        return 'vas262-band-ok';
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
        if (name === 'dollar') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="12" y1="1" x2="12" y2="23"></line><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"></path></svg>';
        }
        return '';
    }

    // Populates --dash-inline-size (a shared, dashboard-wide CSS var read by
    // .vas262-root's own clamp() font-size formula) from the actual dashboard
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

    VAS.VAS_262_ValueByTypeWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas262-root">');
        var $sub, $segs;

        var currentTypeCode = '', currentTypeLabel = '';

        var $list, $listBody, $listPager, $listCount;
        var listOffset = 0, listTotal = 0, listSeq = 0;

        var $detail, $detailBody, $detailMsg;
        var detailBusy = false;

        var $confirm, $confirmMsg;
        var confirmCallback = null;

        var zoomWindowId = ZOOM_WINDOW_ID;

        /* ---------- Widget header + ranked bars ---------- */

        function createWidget() {
            $root.html(
                '<div class="vas262-head">' +
                    '<span class="vas262-iconwell">' + icon('dollar') + '</span>' +
                    '<span class="vas262-head-text">' +
                        '<span class="vas262-title">' + escapeHtml(label('VAS_262_Title', 'Value by Active Contract Type')) + '</span>' +
                        '<span class="vas262-sub vas262-skel-sub">&nbsp;</span>' +
                    '</span>' +
                '</div>' +
                '<div class="vas262-body"><div class="vas262-segs"></div></div>'
            );
            $sub = $root.find('.vas262-sub');
            $segs = $root.find('.vas262-segs');
            $root.on('click', '[data-open-type]', function () {
                openList($(this).attr('data-open-type'), $(this).attr('data-open-label'));
            });
        }

        function renderError() {
            $sub.removeClass('vas262-skel-sub').text(label('VAS_262_LoadError', "Couldn't load"));
            $segs.html('<div class="vas262-state">' + escapeHtml(label('VAS_262_UnableToLoad', 'Unable to load contracts.')) + '</div>');
        }

        function segHtml(row, color, pct) {
            return '<div class="vas262-seg">' +
                '<div class="vas262-seg-top">' +
                    '<span class="vas262-seg-l" data-open-type="' + escapeHtml(row.TypeCode) + '" data-open-label="' + escapeHtml(row.TypeLabel) + '">' +
                        '<span class="vas262-sw" style="background:' + color + '"></span>' +
                        escapeHtml(row.TypeLabel) +
                    '</span>' +
                    '<span class="vas262-seg-r"><span class="vas262-seg-v">' + escapeHtml(formatMoney(row.TypeValueBase, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span> · ' + formatCount(row.ContractCount) + '</span>' +
                '</div>' +
                '<div class="vas262-bar"><div style="width:' + pct + '%;background:' + color + '"></div></div>' +
            '</div>';
        }

        function renderBreakdown(data) {
            var currencyIso = data.CurrencyIso, currencySymbol = data.CurrencySymbol, currencyPrecision = data.CurrencyPrecision;
            $sub.removeClass('vas262-skel-sub').text(
                formatMoney(data.TotalValueBase, currencyIso, currencySymbol, currencyPrecision) + ' ' + label('VAS_262_SubSuffix', 'across ContractType'));

            var types = data.Types || [];
            if (!types.length) {
                $segs.html('<div class="vas262-state">' + escapeHtml(label('VAS_262_Nothing', 'Nothing here right now.')) + '</div>');
                return;
            }

            var max = types.reduce(function (m, t) { return Math.max(m, Number(t.TypeValueBase || 0)); }, 1);

            var html = types.map(function (row, i) {
                row.CurrencyIso = currencyIso; row.CurrencySymbol = currencySymbol; row.CurrencyPrecision = currencyPrecision;
                var pct = Math.round((Number(row.TypeValueBase || 0) / max) * 100);
                return segHtml(row, typeColor(row.TypeLabel, i), pct);
            }).join('');

            $segs.html(html);
        }

        function loadBreakdown() {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetValueByTypeBreakdown',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { renderError(); return; }
                    renderBreakdown(parsed);
                },
                error: function () { renderError(); }
            });
        }

        /* ---------- Per-type drill list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas262-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas262-scrim" data-list-close></div>' +
                    '<section class="vas262-panel">' +
                        '<header class="vas262-phead">' +
                            '<h2 class="vas262-ptitle"></h2>' +
                            '<span class="vas262-pcount"></span>' +
                            '<button type="button" class="vas262-close" data-list-close aria-label="' + escapeHtml(label('VAS_262_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas262-colhead">' +
                            '<span>' + escapeHtml(label('VAS_262_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas262-col-r">' + escapeHtml(label('VAS_262_ColValue', 'Value')) + '</span>' +
                            '<span class="vas262-col-r">' + escapeHtml(label('VAS_262_ColEnds', 'Ends')) + '</span>' +
                            '<span>' + escapeHtml(label('VAS_262_ColRenewal', 'Renewal')) + '</span>' +
                            '<span class="vas262-col-r">' + escapeHtml(label('VAS_262_ColStatus', 'Status')) + '</span>' +
                        '</div>' +
                        '<div class="vas262-pbody"></div>' +
                        '<footer class="vas262-pfoot">' +
                            '<div class="vas262-pager"></div>' +
                            '<div class="vas262-pfoot-actions">' +
                                '<button type="button" class="vas262-btn vas262-btn-ghost" data-list-close>' + escapeHtml(label('VAS_262_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas262-btn vas262-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_262_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas262-pbody');
            $listPager = $list.find('.vas262-pager');
            $listCount = $list.find('.vas262-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas262-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas262-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas262', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function openList(typeCode, typeLabel) {
            currentTypeCode = typeCode;
            currentTypeLabel = typeLabel;
            listOffset = 0;
            $list.find('.vas262-ptitle').text(label('VAS_262_ListTitlePrefix', 'Type ·') + ' ' + typeLabel);
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas262-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas262-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas262-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetValueByTypeDrill',
                type: 'GET', dataType: 'json', cache: false,
                data: { typeCode: currentTypeCode, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas262-state">' + escapeHtml(label('VAS_262_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_262_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas262-state">' + escapeHtml(label('VAS_262_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            return '<div class="vas262-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas262-mtc-main">' +
                    '<span class="vas262-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas262-mtc-text">' +
                        '<span class="vas262-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas262-row-meta">' + escapeHtml([row.DocumentNo, row.ProductName].filter(function (p) { return p; }).join(' · ')) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas262-col-r"><b>' + escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</b></span>' +
                '<span class="vas262-col-r">' + escapeHtml(Number(row.DaysToEnd || 0) < 0 ? formatCount(-row.DaysToEnd) + label('VAS_262_DaysAgoSuffix', 'd ago') : formatCount(row.DaysToEnd) + label('VAS_262_DaysSuffix', 'd')) + '</span>' +
                '<span><span class="vas262-renewal ' + renewalClass(row.RenewalTypeCode) + '"><span class="vas262-dot"></span>' + escapeHtml(row.RenewalType || '') + '</span></span>' +
                '<span class="vas262-col-r"><span class="vas262-status ' + rowLifecycleClass(row.DaysToEnd) + '">' + escapeHtml(rowLifecycleStatus(row.DaysToEnd)) + '</span></span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas262-state">' + escapeHtml(label('VAS_262_Empty', 'No contracts.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_262_Of', 'of');

            $listPager.html(
                '<span class="vas262-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas262-pgctl">' +
                    '<button type="button" class="vas262-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_262_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas262-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_262_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
        // to exactly the clicked type's population, fetched as a plain Contract_ID
        // list (GetValueByTypeIds) rather than reconstructed as a raw where
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetValueByTypeIds',
                        type: 'GET', dataType: 'json', cache: false,
                        data: { typeCode: currentTypeCode },
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
                '<div class="vas262-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas262-scrim" data-detail-close></div>' +
                    '<section class="vas262-panel">' +
                        '<header class="vas262-mhead">' +
                            '<h2 class="vas262-mtitle">' + escapeHtml(label('VAS_262_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas262-mhead-meta"></span>' +
                            '<button type="button" class="vas262-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_262_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas262-mbody"></div>' +
                        '<div class="vas262-mmsg" role="status"></div>' +
                        '<footer class="vas262-mfoot">' +
                            '<button type="button" class="vas262-btn vas262-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_262_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas262-btn vas262-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_262_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas262-btn vas262-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_262_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas262-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_262_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_262_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas262-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas262-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas262-confirm-box">' +
                        '<p class="vas262-confirm-msg"></p>' +
                        '<div class="vas262-confirm-actions">' +
                            '<button type="button" class="vas262-btn vas262-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_262_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas262-btn vas262-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_262_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas262-confirm-msg');
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
            return '<div class="vas262-stat">' +
                '<span class="vas262-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas262-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
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
            $detailBody = $detail.find('.vas262-mbody');
            $detail.find('.vas262-mhead-meta').empty();
            $detailBody.html('<div class="vas262-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas262-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas262-state">' + escapeHtml(label('VAS_262_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas262-state">' + escapeHtml(label('VAS_262_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            if (isManualRenewal(row.RenewalTypeCode) && !row.HasSuccessor && row.NoticeSlackDays != null && row.NoticeSlackDays <= 0) {
                var noticeTitle = row.NoticeSlackDays < 0
                    ? label('VAS_262_RenewalNoticeOverdue', 'Renewal notice overdue')
                    : label('VAS_262_RenewalNoticeDueToday', 'Renewal notice due today');
                var noticeDescParts = [label('VAS_262_ManualRenewal', 'Manual renewal')];
                if (row.NoticeSlackDays < 0) {
                    noticeDescParts.push(label('VAS_262_NoticeOverdueBy', 'notice {n}d overdue').replace('{n}', formatCount(-row.NoticeSlackDays)));
                }
                noticeDescParts.push(label('VAS_262_EndsPrefix', 'ends') + ' ' + endsText(row.DaysToEnd));
                cards += '<div class="vas262-attn-card">' +
                    '<span class="vas262-attn-ic vas262-attn-ic-danger">' + icon('warning') + '</span>' +
                    '<span class="vas262-attn-main">' +
                        '<span class="vas262-attn-title2">' + escapeHtml(noticeTitle) + '</span>' +
                        '<span class="vas262-attn-desc">' + escapeHtml(noticeDescParts.join(' · ')) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_262_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_262_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas262-attn-card">' +
                    '<span class="vas262-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas262-attn-main">' +
                        '<span class="vas262-attn-title2">' + escapeHtml(label('VAS_262_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas262-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas262-attn-empty">' + escapeHtml(label('VAS_262_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas262-attn">' +
                '<div class="vas262-attn-title">' + escapeHtml(label('VAS_262_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_262_Period', 'Period');
            var invoicedLabel = label('VAS_262_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_262_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas262-band-ok' : 'vas262-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas262-sched-row">' +
                    '<span class="vas262-sched-left">' + left + '</span>' +
                    '<span class="vas262-sched-right">' +
                        '<span class="vas262-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas262-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas262-sched">' +
                '<div class="vas262-sched-title">' + escapeHtml(label('VAS_262_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas262-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas262-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_262_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_262_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas262-dtop2">' +
                    '<span class="vas262-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas262-dhead-main">' +
                        '<span class="vas262-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas262-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas262-dhead-right">' +
                        '<span class="vas262-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas262-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas262-stats">' +
                    statHtml('VAS_262_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_262_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_262_Type', 'Type', row.ContractType) +
                    statHtml('VAS_262_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_262_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_262_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_262_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_262_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
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
                showDetailMessage(label('VAS_262_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_262_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadBreakdown();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_262_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas262-modal-open'); }
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
            loadBreakdown();
        };

        this.refreshWidget = function () { loadBreakdown(); };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas262');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas262-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_262_ValueByTypeWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_262_ValueByTypeWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_262_ValueByTypeWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_262_ValueByTypeWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_262_ValueByTypeWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_262_ValueByTypeWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
