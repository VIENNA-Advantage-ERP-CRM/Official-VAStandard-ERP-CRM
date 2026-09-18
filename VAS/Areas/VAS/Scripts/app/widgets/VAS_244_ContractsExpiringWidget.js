/**
 * VAS_244 Contracts Expiring ≤90 days KPI Widget (Service Contracts dashboard)
 * Purpose - 2x1 CLICKABLE KPI tile: the COUNT of live contracts ending within
 *           90 days, with the base-currency value at stake as the sub-line and
 *           an informational "renew" tag. Clicking opens the paged (@7,
 *           soonest-ending first) "Contracts expiring ≤90 days" drill list; a
 *           row opens the shared contract detail modal (reusing
 *           VAS_241_ContractSearchWidget's GetContract / RunRenew /
 *           RunGenerateInvoice endpoints, per the established cross-widget
 *           endpoint-reuse pattern VAS_120 already set with VAS_126's
 *           SaveActivityLog), whose "Open record" zooms to the Service
 *           Contract window.
 * Design  - service-contracts-dashboard.html was not attached with this build
 *           request - only the two reference screenshots were. The tile and
 *           drill-list layout below follow those screenshots pixel-for-pixel
 *           (column layout, avatar chips, renewal dot-badges, pager style,
 *           "Open in browser" footer action); re-verify against the real mock
 *           file if it becomes available.
 * Scope   - "Open in browser" (drill-list footer) opens the Service Contract
 *           window directly, no record/filter, rather than reconstructing the
 *           ≤90-day filter as a client-side where clause: this codebase's own
 *           VAS_140 widget documents that inlining relative date arithmetic
 *           into a raw SQL/where fragment is exactly the kind of thing that
 *           breaks across Postgres/Oracle, and neither companion .md file
 *           specifies this button's exact behaviour (only the screenshot
 *           shows it) - "just open the window" is the safe, well-precedented
 *           reading. 2026-09-08: when the widget sits on a host window
 *           (windowNo >= 0), this now goes through widgetFirevalueChanged /
 *           ActionName - the SAME channel "Open record" already used - rather
 *           than VAS.ZoomUtil's hardcoded AD_Window_ID 1000248, which turned
 *           out to resolve to a different window (Lead) than intended on the
 *           real install; ActionName is resolved by NAME through the host
 *           window framework, so it is not exposed to that mismatch. The
 *           ZoomUtil/1000248 call remains only as the best-effort fallback
 *           for the (unhosted) Home-page placement, matching VAS_126's
 *           openInBrowser precedent exactly (VAS_126_OpenTicketsWidget).
 *
 * Backend - VAS_244_ContractsExpiringWidget/GetExpiringSummary   (GET -> ExpiringCount, ExpiringValueBase, currency)
 *           VAS_244_ContractsExpiringWidget/GetExpiringContracts (GET offset -> rows[≤7] + total)
 *           VAS_241_ContractSearchWidget/GetContract              (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                 (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice       (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Expiring ≤90 Days                     | VAS_244_Title
 *  2  | at stake · click                      | VAS_244_AtStake
 *  3  | renew                                 | VAS_244_RenewTag
 *  4  | Couldn't load                         | VAS_244_LoadError
 *  5  | Contracts Expiring ≤90 Days            | VAS_244_ListTitle
 *  6  | contracts                             | VAS_244_ContractsCount
 *  7  | Contract                              | VAS_244_ColContract
 *  8  | Value                                 | VAS_244_ColValue
 *  9  | Ends                                  | VAS_244_ColEnds
 * 10  | Renewal                               | VAS_244_ColRenewal
 * 11  | Status                                | VAS_244_ColStatus
 * 12  | Expiring                              | VAS_244_StatusExpiring
 * 13  | of                                    | VAS_244_Of
 * 14  | Previous page                         | VAS_244_PrevPage
 * 15  | Next page                             | VAS_244_NextPage
 * 16  | No contracts.                         | VAS_244_NoContracts
 * 17  | Unable to load contracts.             | VAS_244_UnableToLoad
 * 18  | Close                                 | VAS_244_Close
 * 19  | Open in browser                       | VAS_244_OpenInBrowser
 * 20  | Service Contract                      | VAS_244_ContractTitle
 * 21  | Open record                           | VAS_244_OpenRecord
 * 22  | rep                                   | VAS_244_Rep
 * 23  | cycles                                | VAS_244_Cycles
 * 24  | Contract cycle value                  | VAS_244_Value
 * 25  | Status                                | VAS_244_Status
 * 26  | Type                                  | VAS_244_Type
 * 27  | Renewal                               | VAS_244_Renewal
 * 28  | Ends                                  | VAS_244_Ends
 * 29  | in {n}d                               | VAS_244_EndsIn
 * 30  | Ended {n}d ago                        | VAS_244_EndedAgo
 * 31  | Notice days                           | VAS_244_NoticeDays
 * 32  | Billed amount                         | VAS_244_Billed
 * 33  | Unbilled amount                       | VAS_244_Unbilled
 * 34  | What needs attention                  | VAS_244_NeedsAttention
 * 35  | Billing overdue                       | VAS_244_BillingOverdue
 * 36  | Next period {n}d overdue              | VAS_244_NextPeriodOverdue
 * 37  | unbilled total                        | VAS_244_UnbilledTotal
 * 38  | open tickets for this customer        | VAS_244_OpenTickets
 * 39  | Billing schedule                      | VAS_244_BillingSchedule
 * 40  | Period                                | VAS_244_Period
 * 41  | Invoiced                              | VAS_244_Invoiced
 * 42  | Unbilled                              | VAS_244_ScheduleUnbilled
 * 43  | Generate invoice                      | VAS_244_GenerateInvoice
 * 44  | Renew                                 | VAS_244_Renew
 * 45  | Run RenewContract for this contract now? | VAS_244_ConfirmRenew
 * 46  | Generate invoices for the overdue billing periods now? | VAS_244_ConfirmGenerateInvoice
 * 47  | Working…                              | VAS_244_Working
 * 48  | The action failed.                    | VAS_244_ActionFailed
 * 49  | Couldn't load this contract.          | VAS_244_DetailLoadError
 * 50  | d (days-to-end suffix)                | VAS_244_DaysSuffix
 * 50a | d ago (past days-to-end suffix)       | VAS_244_DaysAgoSuffix
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Service Contract window (Export_ID VAS_1000262 / AD_Window_ID 1000248) -
    // the fixed zoom target every Service Contracts widget on this dashboard
    // shares (VAS_241/VAS_242).
    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    // The detail modal + its mutating actions are served by VAS_241 (shared
    // endpoint reuse - see the file header).
    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_244_ContractsExpiringWidget/';

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
        if (d < 0) { return label('VAS_244_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_244_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    // Days-to-end pill colour family - mirrors the rest of the dashboard's
    // urgency banding (≤30d red, 31-90d amber, beyond green, past = cold).
    function daysClass(days) {
        var d = Number(days || 0);
        if (d < 0) { return 'vas244-days-cold'; }
        if (d <= 30) { return 'vas244-days-red'; }
        if (d <= 90) { return 'vas244-days-amber'; }
        return 'vas244-days-green';
    }

    // Renewal dot-badge colour by the returned RenewalType CODE (never the
    // translated label - the badge text itself is always the tenant's own
    // decoded AD_Ref_List(_Trl) name from the server).
    function renewalClass(code) {
        var c = String(code || '').toUpperCase();
        if (c === 'A' || c === 'ATC') { return 'vas244-renewal-auto'; }
        if (c === 'M' || c === 'MNL') { return 'vas244-renewal-manual'; }
        return 'vas244-renewal-neutral';
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas244-band-ok'; }
        if (code === 'EXPIRING') { return 'vas244-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas244-band-danger'; }
        return 'vas244-band-info';
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
        if (name === 'doc') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><path d="M14 2v6h6"></path></svg>';
        }
        return '';
    }

    // Populates --dash-inline-size (a shared, dashboard-wide CSS var read by
    // .vas244-root's own clamp() font-size formulas) from the actual dashboard
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

    VAS.VAS_244_ContractsExpiringWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas244-root">');
        var $card, $value, $sub;

        // Drill list modal.
        var $list, $listBody, $listPager, $listCount;
        var listOffset = 0, listTotal = 0, listSeq = 0;

        // Contract detail modal (shared shape with VAS_241, own endpoint calls).
        var $detail, $detailBody, $detailMsg;
        var detailBusy = false;

        var $confirm, $confirmMsg;
        var confirmCallback = null;

        var zoomWindowId = ZOOM_WINDOW_ID;

        /* ---------- KPI tile ---------- */

        function createWidget() {
            $card = $(
                '<button type="button" class="vas244-card">' +
                    '<span class="vas244-label">' + escapeHtml(label('VAS_244_Title', 'Expiring ≤90 Days')) + ' ' + icon('arrow') + '</span>' +
                    '<span class="vas244-valwrap">' +
                        '<span class="vas244-value vas244-skel-value">&nbsp;</span>' +
                        '<span class="vas244-foot">' +
                            '<span class="vas244-sub vas244-skel-sub">&nbsp;</span>' +
                            '<span class="vas244-tag">' + escapeHtml(label('VAS_244_RenewTag', 'renew')) + '</span>' +
                        '</span>' +
                    '</span>' +
                '</button>'
            );
            $value = $card.find('.vas244-value');
            $sub = $card.find('.vas244-sub');
            $card.on('click', function () { openList(); });
            $root.append($card);
        }

        function renderLoading() {
            $value.addClass('vas244-skel-value').text(' ');
            $sub.addClass('vas244-skel-sub').text(' ');
            $card.removeClass('is-error');
        }

        function renderError() {
            $value.removeClass('vas244-skel-value').text('—');
            $sub.removeClass('vas244-skel-sub').text(label('VAS_244_LoadError', "Couldn't load"));
            $card.addClass('is-error');
        }

        function renderSummary(data) {
            $card.removeClass('is-error');
            $value.removeClass('vas244-skel-value').text(formatCount(data.ExpiringCount));
            $sub.removeClass('vas244-skel-sub').text(
                formatMoney(data.ExpiringValueBase, data.CurrencyIso, data.CurrencySymbol, data.CurrencyPrecision) +
                ' ' + label('VAS_244_AtStake', 'at stake · click'));
        }

        function loadSummary() {
            renderLoading();
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetExpiringSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { renderError(); return; }
                    renderSummary(parsed);
                },
                error: function () { renderError(); }
            });
        }

        /* ---------- Drill list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas244-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas244-scrim" data-list-close></div>' +
                    '<section class="vas244-panel">' +
                        '<header class="vas244-phead">' +
                            '<h2 class="vas244-ptitle">' + escapeHtml(label('VAS_244_ListTitle', 'Contracts Expiring ≤90 Days')) + '</h2>' +
                            '<span class="vas244-pcount"></span>' +
                            '<button type="button" class="vas244-close" data-list-close aria-label="' + escapeHtml(label('VAS_244_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas244-colhead">' +
                            '<span>' + escapeHtml(label('VAS_244_ColContract', 'Contract')) + '</span>' +
                            '<span class="vas244-col-r">' + escapeHtml(label('VAS_244_ColValue', 'Value')) + '</span>' +
                            '<span class="vas244-col-r">' + escapeHtml(label('VAS_244_ColEnds', 'Ends')) + '</span>' +
                            '<span>' + escapeHtml(label('VAS_244_ColRenewal', 'Renewal')) + '</span>' +
                            '<span class="vas244-col-r">' + escapeHtml(label('VAS_244_ColStatus', 'Status')) + '</span>' +
                        '</div>' +
                        '<div class="vas244-pbody"></div>' +
                        '<footer class="vas244-pfoot">' +
                            '<div class="vas244-pager"></div>' +
                            '<div class="vas244-pfoot-actions">' +
                                '<button type="button" class="vas244-btn vas244-btn-ghost" data-list-close>' + escapeHtml(label('VAS_244_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas244-btn vas244-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_244_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas244-pbody');
            $listPager = $list.find('.vas244-pager');
            $listCount = $list.find('.vas244-pcount');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas244-row', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas244-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas244', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        function openList() {
            listOffset = 0;
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas244-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas244-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas244-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetExpiringContracts',
                type: 'GET', dataType: 'json', cache: false,
                data: { offset: listOffset },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas244-state">' + escapeHtml(label('VAS_244_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_244_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas244-state">' + escapeHtml(label('VAS_244_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function rowHtml(row) {
            var meta = [row.DocumentNo, row.ProductName].filter(function (p) { return p; }).join(' · ');
            return '<div class="vas244-row" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas244-row-main">' +
                    '<span class="vas244-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas244-row-text">' +
                        '<span class="vas244-row-name" title="' + escapeHtml(row.CustomerName || '') + '">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas244-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas244-row-val vas244-col-r">' + escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                '<span class="vas244-row-ends vas244-col-r">' + escapeHtml(Number(row.DaysToEnd || 0) < 0 ? formatCount(-row.DaysToEnd) + label('VAS_244_DaysAgoSuffix', 'd ago') : formatCount(row.DaysToEnd) + label('VAS_244_DaysSuffix', 'd')) + '</span>' +
                '<span class="vas244-renewal ' + renewalClass(row.RenewalTypeCode) + '"><span class="vas244-dot"></span>' + escapeHtml(row.RenewalType || '') + '</span>' +
                '<span class="vas244-col-r"><span class="vas244-status">' + escapeHtml(label('VAS_244_StatusExpiring', 'Expiring')) + '</span></span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas244-state">' + escapeHtml(label('VAS_244_NoContracts', 'No contracts.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(rowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_244_Of', 'of');

            $listPager.html(
                '<span class="vas244-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas244-pgctl">' +
                    '<button type="button" class="vas244-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_244_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas244-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_244_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
        // to exactly this KPI's live-and-expiring population, fetched as a plain
        // Contract_ID list (GetExpiringContractIds) rather than reconstructed as
        // a raw DAYSBETWEEN/CURRENT_DATE where fragment client-side (see the
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetExpiringContractIds',
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
                '<div class="vas244-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas244-scrim" data-detail-close></div>' +
                    '<section class="vas244-panel">' +
                        '<header class="vas244-mhead">' +
                            '<h2 class="vas244-mtitle">' + escapeHtml(label('VAS_244_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas244-mhead-meta"></span>' +
                            '<button type="button" class="vas244-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_244_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas244-mbody"></div>' +
                        '<div class="vas244-mmsg" role="status"></div>' +
                        '<footer class="vas244-mfoot">' +
                            '<button type="button" class="vas244-btn vas244-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_244_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas244-btn vas244-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_244_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas244-btn vas244-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_244_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas244-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_244_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_244_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas244-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas244-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas244-confirm-box">' +
                        '<p class="vas244-confirm-msg"></p>' +
                        '<div class="vas244-confirm-actions">' +
                            '<button type="button" class="vas244-btn vas244-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_244_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas244-btn vas244-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_244_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas244-confirm-msg');
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
            return '<div class="vas244-stat">' +
                '<span class="vas244-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas244-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
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
            $detailBody = $detail.find('.vas244-mbody');
            $detail.find('.vas244-mhead-meta').empty();
            $detailBody.html('<div class="vas244-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas244-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas244-state">' + escapeHtml(label('VAS_244_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas244-state">' + escapeHtml(label('VAS_244_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        // Manual-renewal notice window (kpi-renewal-action.queries.md): a manual
        // (never auto) contract with no active successor whose notice window is
        // already open (NoticeSlackDays = daysToEnd - CancelBeforeDays <= 0) needs
        // action - shown first since it is the most time-critical card.
        function isManualRenewal(code) {
            var c = String(code || '').toUpperCase();
            return c === 'M' || c === 'MNL';
        }

        function attentionCards(row) {
            var cards = '';

            if (isManualRenewal(row.RenewalTypeCode) && !row.HasSuccessor && row.NoticeSlackDays != null && row.NoticeSlackDays <= 0) {
                var noticeTitle = row.NoticeSlackDays < 0
                    ? label('VAS_244_RenewalNoticeOverdue', 'Renewal notice overdue')
                    : label('VAS_244_RenewalNoticeDueToday', 'Renewal notice due today');
                var noticeDescParts = [label('VAS_244_ManualRenewal', 'Manual renewal')];
                if (row.NoticeSlackDays < 0) {
                    noticeDescParts.push(label('VAS_244_NoticeOverdueBy', 'notice {n}d overdue').replace('{n}', formatCount(-row.NoticeSlackDays)));
                }
                noticeDescParts.push(label('VAS_244_EndsPrefix', 'ends') + ' ' + endsText(row.DaysToEnd));
                cards += '<div class="vas244-attn-card">' +
                    '<span class="vas244-attn-ic vas244-attn-ic-danger">' + icon('warning') + '</span>' +
                    '<span class="vas244-attn-main">' +
                        '<span class="vas244-attn-title2">' + escapeHtml(noticeTitle) + '</span>' +
                        '<span class="vas244-attn-desc">' + escapeHtml(noticeDescParts.join(' · ')) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_244_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_244_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas244-attn-card">' +
                    '<span class="vas244-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas244-attn-main">' +
                        '<span class="vas244-attn-title2">' + escapeHtml(label('VAS_244_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas244-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas244-attn-empty">' + escapeHtml(label('VAS_244_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas244-attn">' +
                '<div class="vas244-attn-title">' + escapeHtml(label('VAS_244_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_244_Period', 'Period');
            var invoicedLabel = label('VAS_244_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_244_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas244-band-ok' : 'vas244-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas244-sched-row">' +
                    '<span class="vas244-sched-left">' + left + '</span>' +
                    '<span class="vas244-sched-right">' +
                        '<span class="vas244-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas244-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas244-sched">' +
                '<div class="vas244-sched-title">' + escapeHtml(label('VAS_244_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas244-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas244-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_244_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_244_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas244-dtop2">' +
                    '<span class="vas244-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas244-dhead-main">' +
                        '<span class="vas244-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas244-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas244-dhead-right">' +
                        '<span class="vas244-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas244-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas244-stats">' +
                    statHtml('VAS_244_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_244_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_244_Type', 'Type', row.ContractType) +
                    statHtml('VAS_244_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_244_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_244_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_244_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_244_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
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
                showDetailMessage(label('VAS_244_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_244_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadSummary();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_244_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas244-modal-open'); }
        }

        // Resolve the host window name so Scenario 1 in-window zoom navigates
        // the current grid in place (matches VAS_120/241's own zoom helper).
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
            $(document).off('keydown.MPCvas244');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas244-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_244_ContractsExpiringWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_244_ContractsExpiringWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_244_ContractsExpiringWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_244_ContractsExpiringWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_244_ContractsExpiringWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_244_ContractsExpiringWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
