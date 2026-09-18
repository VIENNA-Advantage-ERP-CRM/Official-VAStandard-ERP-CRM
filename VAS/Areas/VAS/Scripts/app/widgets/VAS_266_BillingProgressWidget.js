/**
 * VAS_266 Billing Progress Widget (Service Contracts dashboard)
 * Purpose - c3 x r2 read-only summary widget: billed vs unbilled value (base
 *           currency) across the live contract portfolio - a big "% billed"
 *           figure, a two-segment split bar, a Billed/Unbilled legend and a
 *           two-tile stat row (unbilled contracts / billing overdue). No
 *           always-visible row list and no "All ->" link (this is a summary,
 *           not a list, per the build spec) - a single GetBillingProgressSummary
 *           call drives the whole non-modal body. The legend items and the
 *           two stat tiles are clickable drills, each opening the SAME list
 *           modal against a different server-side predicate (unbilled /
 *           billing overdue / billed), reusing VAS_241_ContractSearchWidget's
 *           GetContract / RunRenew / RunGenerateInvoice endpoints for the row
 *           detail modal - the same cross-widget endpoint-reuse pattern
 *           VAS_120 set with VAS_126 and VAS_244/245/246/258/259/264/265
 *           already reused too. Percent billed is computed server-side in
 *           C# (never in SQL) and simply rendered here. The widget never
 *           resizes on load - fixed c3 x r2 footprint, outer overflow
 *           hidden, the summary content is vertically centred in the body.
 * Design  - billing-progress.html (the parent Service Contracts dashboard
 *           mock, single-widget preview of billingProgressWidget()) is the
 *           pixel-level source of truth; re-created verbatim below (tokens,
 *           prog-big/split/prog-leg/stat3 structure, header icon well/title/
 *           sub with no "All" link). All three drills (unbilled / billing
 *           overdue / billed) share ONE 5-column list-modal shape - Contract
 *           / Value / Ends / Renewal / Status - matching the combined
 *           dashboard mock's own universal drill-list row (its shared
 *           renderList()/openListItems()), not the narrower per-kind money/
 *           date-only shape billing-progress.queries.md's SELECT lists
 *           literally show (2026-09-09 fix: the mock's actual rendered
 *           "Unbilled contracts" / "Billing overdue" dialogs both carry all
 *           five columns; the original build omitted Ends/Renewal/Status).
 *           "Value" is always the contract's own GrandTotal (base currency) -
 *           the drill's own Unbilled/Billed/oldest-overdue-date figure only
 *           drives that drill's WHERE/ORDER server-side, never a displayed
 *           column. "Status" is derived Active/Expiring (every row here is
 *           already live, so no other band can appear); "Renewal" is the
 *           server-decoded AD_Ref_List label, never a hardcoded Auto/Manual.
 * Scope   - "Open in browser" (list-modal footer) zooms to the Service
 *           Contract window filtered down to exactly the CURRENTLY OPEN
 *           drill's population, fetched as a plain Contract_ID list
 *           (GetBillingProgressDrillIds(kind)) rather than reconstructed as
 *           a raw where fragment client-side (this codebase's own VAS_140
 *           widget shows that inlining relative predicates into a raw
 *           where-clause breaks across Postgres/Oracle). When hosted
 *           (windowNo >= 0) this goes through widgetFirevalueChanged /
 *           ActionName - the same channel "Open record" already uses -
 *           resolved by NAME through the host window framework
 *           (VAS_244/245/246/258/259/264/265's own openInBrowser fix,
 *           2026-09-08: VAS.ZoomUtil's hardcoded AD_Window_ID 1000248 turned
 *           out to resolve to a different window (Lead) on the real install).
 *
 * Backend - VAS_266_BillingProgressWidget/GetBillingProgressSummary   (GET -> BilledBase, UnbilledBase, PctBilled, UnbilledContracts, BillingOverdueContracts, currency)
 *           VAS_266_BillingProgressWidget/GetBillingProgressDrill     (GET kind,offset,limit -> rows + total)
 *           VAS_266_BillingProgressWidget/GetBillingProgressDrillIds  (GET kind -> Ids[])
 *           VAS_241_ContractSearchWidget/GetContract                 (GET id -> contract detail + windowId) [reused]
 *           VAS_241_ContractSearchWidget/RunRenew                    (POST id -> { Success, Message })       [reused]
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice          (POST id -> { Success, Message })       [reused]
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+--------------------------------
 *  1  | Billing Progress                      | VAS_266_Title
 *  2  | {v} billed of {t} · CR_SERVICECONTRACT_V | VAS_266_Sub
 *  3  | Couldn't load                         | VAS_266_LoadError
 *  4  | of live contract value invoiced       | VAS_266_ProgCaption
 *  5  | Billed {v}                            | VAS_266_LegendBilled
 *  6  | Unbilled {v}                          | VAS_266_LegendUnbilled
 *  7  | unbilled contracts                    | VAS_266_StatUnbilled
 *  8  | billing overdue                       | VAS_266_StatOverdue
 *  9  | Billed Contracts                      | VAS_266_ListTitleBilled
 * 10  | Unbilled Contracts                    | VAS_266_ListTitleUnbilled
 * 11  | Billing Overdue                       | VAS_266_ListTitleOverdue
 * 12  | contracts                             | VAS_266_ContractsCount
 * 13  | Contract                              | VAS_266_ColContract
 * 14  | Value                                 | VAS_266_ColValue
 * 15  | Ends                                  | VAS_266_ColEnds
 * 16  | Renewal                               | VAS_266_ColRenewal
 * 17  | Status                                | VAS_266_ColStatus
 * 18  | of                                    | VAS_266_Of
 * 19  | Previous page                         | VAS_266_PrevPage
 * 20  | Next page                             | VAS_266_NextPage
 * 21  | Nothing here right now.               | VAS_266_Empty
 * 22  | Unable to load contracts.             | VAS_266_UnableToLoad
 * 23  | Close                                 | VAS_266_Close
 * 24  | Open in browser                       | VAS_266_OpenInBrowser
 * 25  | Service Contract                      | VAS_266_ContractTitle
 * 26  | Open record                           | VAS_266_OpenRecord
 * 27  | rep                                   | VAS_266_Rep
 * 28  | cycles                                | VAS_266_Cycles
 * 29  | Contract cycle value                  | VAS_266_Value
 * 30  | Status                                | VAS_266_Status
 * 31  | Type                                  | VAS_266_Type
 * 32  | Renewal                               | VAS_266_Renewal
 * 33  | Ends                                  | VAS_266_Ends
 * 34  | in {n}d                               | VAS_266_EndsIn
 * 35  | Ended {n}d ago                        | VAS_266_EndedAgo
 * 36  | d                                     | VAS_266_DaysSuffix
 * 37  | d ago                                 | VAS_266_DaysAgoSuffix
 * 38  | Active                                | VAS_266_StatusActive
 * 39  | Expiring                              | VAS_266_StatusExpiring
 * 40  | Notice days                           | VAS_266_NoticeDays
 * 41  | Billed amount                         | VAS_266_DetailBilled
 * 42  | Unbilled amount                       | VAS_266_DetailUnbilled
 * 43  | What needs attention                  | VAS_266_NeedsAttention
 * 44  | No open actions - contract is healthy.| VAS_266_NoOpenActions
 * 45  | Billing overdue                       | VAS_266_SignalBillingOverdue
 * 46  | Next period {n}d overdue              | VAS_266_NextPeriodOverdue
 * 47  | unbilled total                        | VAS_266_UnbilledTotal
 * 48  | open tickets for this customer        | VAS_266_OpenTickets
 * 49  | Billing schedule                      | VAS_266_BillingSchedule
 * 50  | Period                                | VAS_266_Period
 * 51  | Invoiced                              | VAS_266_Invoiced
 * 52  | Pending                               | VAS_266_SchedulePending
 * 53  | Generate invoice                      | VAS_266_GenerateInvoice
 * 54  | Renew                                 | VAS_266_Renew
 * 55  | Run RenewContract for this contract now? | VAS_266_ConfirmRenew
 * 56  | Generate invoices for the overdue billing periods now? | VAS_266_ConfirmGenerateInvoice
 * 57  | Working…                              | VAS_266_Working
 * 58  | The action failed.                    | VAS_266_ActionFailed
 * 59  | Couldn't load this contract.          | VAS_266_DetailLoadError
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var DETAIL_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var SELF_ENDPOINT = 'VAS_266_BillingProgressWidget/';

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

    // Detail-modal "Ends" stat (Sentence case, matching VAS_241/244/245/246/258/259/264/265).
    function endsText(days) {
        var d = Number(days || 0);
        if (d < 0) { return label('VAS_266_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_266_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    // Server-computed LIFECYCLE status code (ACTIVE/EXPIRING/ENDED/CANCELLED/
    // VOIDED/REVERSED/DRAFT) from VAS_241's GetContract - NOT the raw DocStatus.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas266-band-ok'; }
        if (code === 'EXPIRING') { return 'vas266-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas266-band-danger'; }
        return 'vas266-band-info';
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
        if (name === 'trend') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="22 7 13.5 15.5 8.5 10.5 2 17"></polyline><polyline points="16 7 22 7 22 13"></polyline></svg>';
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
    // .vas266-root's own clamp() font-size formulas) from the actual dashboard
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

    VAS.VAS_266_BillingProgressWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas266-root">');
        var $billedFill, $unbilledFill, $pctVal, $legendBilled, $legendUnbilled, $statUnbilled, $statOverdue;

        var $list, $listBody, $listPager, $listCount, $listColhead;
        var listOffset = 0, listTotal = 0, listSeq = 0;
        var currentKind = 'unbilled';
        var currentCurrency = { iso: '', symbol: '', precision: 2 };

        var $detail, $detailBody, $detailMsg;
        var detailBusy = false;

        var $confirm, $confirmMsg;
        var confirmCallback = null;

        var zoomWindowId = ZOOM_WINDOW_ID;

        var LIST_TITLES = {
            unbilled: function () { return label('VAS_266_ListTitleUnbilled', 'Unbilled Contracts'); },
            overdue: function () { return label('VAS_266_ListTitleOverdue', 'Billing Overdue'); },
            billed: function () { return label('VAS_266_ListTitleBilled', 'Billed Contracts'); }
        };

        /* ---------- Widget header + summary body ---------- */

        function createWidget() {
            $root.html(
                '<div class="vas266-head">' +
                    '<div class="vas266-head-l">' +
                        '<span class="vas266-iconwell">' + icon('trend') + '</span>' +
                        '<span class="vas266-head-text">' +
                            '<span class="vas266-title">' + escapeHtml(label('VAS_266_Title', 'Billing Progress')) + '</span>' +
                            '<span class="vas266-sub vas266-skel-sub">&nbsp;</span>' +
                        '</span>' +
                    '</div>' +
                '</div>' +
                '<div class="vas266-body">' +
                    '<div class="vas266-progwrap">' +
                        '<div class="vas266-progbig"><b class="vas266-pct">0%</b><span>' + escapeHtml(label('VAS_266_ProgCaption', 'of live contract value invoiced')) + '</span></div>' +
                        '<div class="vas266-split">' +
                            '<div class="vas266-split-billed" style="width:0%"></div>' +
                            '<div class="vas266-split-unbilled" style="width:0%"></div>' +
                        '</div>' +
                        '<div class="vas266-legend">' +
                            '<span class="vas266-lg" data-drill="billed"><span class="vas266-sw vas266-sw-billed"></span><span class="vas266-lg-billed"></span></span>' +
                            '<span class="vas266-lg" data-drill="unbilled"><span class="vas266-sw vas266-sw-unbilled"></span><span class="vas266-lg-unbilled"></span></span>' +
                        '</div>' +
                        '<div class="vas266-stat3">' +
                            '<div class="vas266-st" data-drill="unbilled"><div class="vas266-sv vas266-sv-unbilled">0</div><div class="vas266-sl">' + escapeHtml(label('VAS_266_StatUnbilled', 'unbilled contracts')) + '</div></div>' +
                            '<div class="vas266-st" data-drill="overdue"><div class="vas266-sv vas266-sv-overdue">0</div><div class="vas266-sl">' + escapeHtml(label('VAS_266_StatOverdue', 'billing overdue')) + '</div></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $billedFill = $root.find('.vas266-split-billed');
            $unbilledFill = $root.find('.vas266-split-unbilled');
            $pctVal = $root.find('.vas266-pct');
            $legendBilled = $root.find('.vas266-lg-billed');
            $legendUnbilled = $root.find('.vas266-lg-unbilled');
            $statUnbilled = $root.find('.vas266-sv-unbilled');
            $statOverdue = $root.find('.vas266-sv-overdue');

            $root.on('click', '[data-drill]', function () { openList($(this).attr('data-drill')); });
        }

        function renderSummaryError() {
            $root.find('.vas266-sub').removeClass('vas266-skel-sub').text(label('VAS_266_LoadError', "Couldn't load"));
        }

        function loadSummary() {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetBillingProgressSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { renderSummaryError(); return; }
                    renderSummary(parsed);
                },
                error: renderSummaryError
            });
        }

        function renderSummary(data) {
            currentCurrency = { iso: data.CurrencyIso, symbol: data.CurrencySymbol, precision: data.CurrencyPrecision };

            var billedText = formatMoney(data.BilledBase, data.CurrencyIso, data.CurrencySymbol, data.CurrencyPrecision);
            var unbilledText = formatMoney(data.UnbilledBase, data.CurrencyIso, data.CurrencySymbol, data.CurrencyPrecision);
            var totalText = formatMoney(Number(data.BilledBase || 0) + Number(data.UnbilledBase || 0), data.CurrencyIso, data.CurrencySymbol, data.CurrencyPrecision);
            var pct = Math.max(0, Math.min(100, Number(data.PctBilled || 0)));

            $root.find('.vas266-sub').removeClass('vas266-skel-sub')
                .text(label('VAS_266_Sub', '{v} billed of {t} · CR_SERVICECONTRACT_V').replace('{v}', billedText).replace('{t}', totalText));

            $pctVal.text(pct + '%');
            $billedFill.css('width', pct + '%');
            $unbilledFill.css('width', (100 - pct) + '%');

            $legendBilled.text(label('VAS_266_LegendBilled', 'Billed {v}').replace('{v}', billedText));
            $legendUnbilled.text(label('VAS_266_LegendUnbilled', 'Unbilled {v}').replace('{v}', unbilledText));

            $statUnbilled.text(formatCount(data.UnbilledContracts));

            var overdueCount = Number(data.BillingOverdueContracts || 0);
            $statOverdue.text(formatCount(overdueCount)).toggleClass('vas266-sv-danger', overdueCount > 0).toggleClass('vas266-sv-ok', overdueCount === 0);
        }

        /* ---------- Shared drill list modal (unbilled / overdue / billed) ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas266-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas266-scrim" data-list-close></div>' +
                    '<section class="vas266-panel">' +
                        '<header class="vas266-phead">' +
                            '<h2 class="vas266-ptitle"></h2>' +
                            '<span class="vas266-pcount"></span>' +
                            '<button type="button" class="vas266-close" data-list-close aria-label="' + escapeHtml(label('VAS_266_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas266-colhead"></div>' +
                        '<div class="vas266-pbody"></div>' +
                        '<footer class="vas266-pfoot">' +
                            '<div class="vas266-pager2"></div>' +
                            '<div class="vas266-pfoot-actions">' +
                                '<button type="button" class="vas266-btn vas266-btn-ghost" data-list-close>' + escapeHtml(label('VAS_266_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas266-btn vas266-btn-primary" data-open-browser>' + icon('open') + escapeHtml(label('VAS_266_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas266-pbody');
            $listPager = $list.find('.vas266-pager2');
            $listCount = $list.find('.vas266-pcount');
            $listColhead = $list.find('.vas266-colhead');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '[data-open-browser]', function () { openInBrowser(); });
            $list.on('click', '.vas266-mtrow', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas266-pgbtn', function () { turnListPage($(this).attr('data-dir')); });
            $(document).on('keydown.MPCvas266', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list.hasClass('is-open')) { closeList(); }
            });
        }

        // Column header is fixed (Contract / Value / Ends / Renewal / Status)
        // across all three drills - the combined dashboard mock's own
        // universal drill-list shape (2026-09-09 fix; see the controller's
        // GetDrillData doc comment for why this replaced the earlier per-kind
        // money/date-only shape).
        function renderColhead() {
            $listColhead.html(
                '<span>' + escapeHtml(label('VAS_266_ColContract', 'Contract')) + '</span>' +
                '<span class="vas266-col-r">' + escapeHtml(label('VAS_266_ColValue', 'Value')) + '</span>' +
                '<span class="vas266-col-r">' + escapeHtml(label('VAS_266_ColEnds', 'Ends')) + '</span>' +
                '<span class="vas266-col-r">' + escapeHtml(label('VAS_266_ColRenewal', 'Renewal')) + '</span>' +
                '<span class="vas266-col-r">' + escapeHtml(label('VAS_266_ColStatus', 'Status')) + '</span>'
            );
        }

        function openList(kind) {
            currentKind = kind || 'unbilled';
            listOffset = 0;
            $list.find('.vas266-ptitle').text((LIST_TITLES[currentKind] || LIST_TITLES.unbilled)());
            renderColhead();
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas266-modal-open');
            loadList();
        }

        function closeList() {
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas266-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas266-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetBillingProgressDrill',
                type: 'GET', dataType: 'json', cache: false,
                data: { kind: currentKind, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas266-state">' + escapeHtml(label('VAS_266_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    $listCount.text(formatCount(listTotal) + ' ' + label('VAS_266_ContractsCount', 'contracts'));
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas266-state">' + escapeHtml(label('VAS_266_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        // Renewal chip - dot colour keyed off the server-resolved code (A =
        // green, else amber), text = the server-decoded AD_Ref_List label
        // (never a hardcoded "Auto"/"Manual" - falls back to the raw code
        // only if the decode itself came back empty).
        function renewalChipHtml(code, label_) {
            var isAuto = String(code || '').toUpperCase() === 'A';
            return '<span class="vas266-chip ' + (isAuto ? 'vas266-chip-auto' : 'vas266-chip-manual') + '">' +
                '<span class="vas266-dot"></span>' + escapeHtml(label_ || code || '') +
            '</span>';
        }

        function statusChipHtml(lifecycleStatusCode) {
            var code = String(lifecycleStatusCode || '').toUpperCase();
            var text = code === 'EXPIRING' ? label('VAS_266_StatusExpiring', 'Expiring') : label('VAS_266_StatusActive', 'Active');
            return '<span class="vas266-band ' + bandClass(lifecycleStatusCode) + '">' + escapeHtml(text) + '</span>';
        }

        function listRowHtml(row) {
            return '<div class="vas266-mtrow" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas266-mtc-main">' +
                    '<span class="vas266-avatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas266-mtc-text">' +
                        '<span class="vas266-row-name">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas266-row-meta">' + escapeHtml([row.DocumentNo, row.ProductName].filter(function (p) { return p; }).join(' · ')) + '</span>' +
                    '</span>' +
                '</span>' +
                '<span class="vas266-col-r"><b>' + escapeHtml(formatMoney(row.GrandTotalBase, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</b></span>' +
                '<span class="vas266-col-r">' + escapeHtml(Number(row.DaysToEnd || 0) < 0 ? formatCount(-row.DaysToEnd) + label('VAS_266_DaysAgoSuffix', 'd ago') : formatCount(row.DaysToEnd) + label('VAS_266_DaysSuffix', 'd')) + '</span>' +
                '<span class="vas266-col-r">' + renewalChipHtml(row.RenewalTypeCode, row.RenewalTypeLabel) + '</span>' +
                '<span class="vas266-col-r">' + statusChipHtml(row.LifecycleStatusCode) + '</span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas266-state">' + escapeHtml(label('VAS_266_Empty', 'Nothing here right now.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html(rows.map(listRowHtml).join(''));

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_266_Of', 'of');

            $listPager.html(
                '<span class="vas266-pglabel">' + escapeHtml(start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas266-pgctl">' +
                    '<button type="button" class="vas266-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_266_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<button type="button" class="vas266-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_266_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
        // to exactly the currently open drill's population, fetched as a plain
        // Contract_ID list (GetBillingProgressDrillIds) rather than
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
                        url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetBillingProgressDrillIds',
                        type: 'GET', dataType: 'json', cache: false,
                        data: { kind: currentKind },
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
                '<div class="vas266-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas266-scrim" data-detail-close></div>' +
                    '<section class="vas266-panel">' +
                        '<header class="vas266-mhead">' +
                            '<h2 class="vas266-mtitle">' + escapeHtml(label('VAS_266_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas266-mhead-meta"></span>' +
                            '<button type="button" class="vas266-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_266_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas266-mbody"></div>' +
                        '<div class="vas266-mmsg" role="status"></div>' +
                        '<footer class="vas266-mfoot">' +
                            '<button type="button" class="vas266-btn vas266-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_266_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas266-btn vas266-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_266_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas266-btn vas266-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_266_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas266-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_266_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_266_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas266-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas266-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas266-confirm-box">' +
                        '<p class="vas266-confirm-msg"></p>' +
                        '<div class="vas266-confirm-actions">' +
                            '<button type="button" class="vas266-btn vas266-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_266_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas266-btn vas266-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_266_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas266-confirm-msg');
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
            return '<div class="vas266-stat">' +
                '<span class="vas266-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas266-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
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
            $detailBody = $detail.find('.vas266-mbody');
            $detail.find('.vas266-mhead-meta').empty();
            $detailBody.html('<div class="vas266-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas266-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + DETAIL_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (parsed && parsed.WindowId) { zoomWindowId = Number(parsed.WindowId) || ZOOM_WINDOW_ID; }
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas266-state">' + escapeHtml(label('VAS_266_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas266-state">' + escapeHtml(label('VAS_266_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        function attentionCards(row) {
            var cards = '';

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_266_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_266_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas266-attn-card">' +
                    '<span class="vas266-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas266-attn-main">' +
                        '<span class="vas266-attn-title2">' + escapeHtml(label('VAS_266_SignalBillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas266-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas266-attn-empty">' + escapeHtml(label('VAS_266_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }
            return '<div class="vas266-attn">' +
                '<div class="vas266-attn-title">' + escapeHtml(label('VAS_266_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_266_Period', 'Period');
            var invoicedLabel = label('VAS_266_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_266_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas266-band-ok' : 'vas266-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas266-sched-row">' +
                    '<span class="vas266-sched-left">' + left + '</span>' +
                    '<span class="vas266-sched-right">' +
                        '<span class="vas266-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas266-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas266-sched">' +
                '<div class="vas266-sched-title">' + escapeHtml(label('VAS_266_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas266-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas266-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_266_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_266_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas266-dtop2">' +
                    '<span class="vas266-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas266-dhead-main">' +
                        '<span class="vas266-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas266-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas266-dhead-right">' +
                        '<span class="vas266-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas266-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas266-stats">' +
                    statHtml('VAS_266_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_266_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_266_Type', 'Type', row.ContractType) +
                    statHtml('VAS_266_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_266_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_266_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_266_DetailBilled', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_266_DetailUnbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
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
                showDetailMessage(label('VAS_266_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + DETAIL_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_266_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                        loadSummary();
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_266_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas266-modal-open'); }
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
            $(document).off('keydown.MPCvas266');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            $('body').removeClass('vas266-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_266_BillingProgressWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_266_BillingProgressWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_266_BillingProgressWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_266_BillingProgressWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_266_BillingProgressWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_266_BillingProgressWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
