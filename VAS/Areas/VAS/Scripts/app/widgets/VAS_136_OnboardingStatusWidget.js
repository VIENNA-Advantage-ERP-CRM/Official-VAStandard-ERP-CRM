/**
 * VAS_136 Onboarding Status Widget (Customers module dashboard)
 * Purpose - 3x1 donut: share of active customers fully onboarded vs still in
 *           progress, with both counts, driven by C_BPartner.VAS_ProfileCompletion
 *           (clamped 0..100; onboarded = 100). Each legend row opens that group's
 *           filtered, paged customer list; a row opens the customer record.
 * Design  - onboarding-status.html (attached) + Design Specs/dashboard-widgets.md.
 *           Glass surface, icon-well header, SVG ring (onboarded #20A464 /
 *           in-progress #D78B10) with the onboarded percent in the centre and a
 *           two-row legend. Internal sizing in em; SVG strokes/borders in px.
 *           CSS namespaced vas136-* (Prompt_Instructions MPC prefix rule).
 *
 * Backend - VAS_136_OnboardingStatusWidget/GetSummary
 *           VAS_136_OnboardingStatusWidget/GetList  (status=done|incomplete, paged)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                      | Message Key
 * ----+----------------------------------+--------------------------------
 *  1  | Onboarding status                | VAS_136_OnboardingStatus
 *  2  | complete                         | VAS_136_Complete
 *  3  | Onboarded                        | VAS_136_Onboarded
 *  4  | In progress                      | VAS_136_InProgress
 *  5  | Onboarded customers              | VAS_136_OnboardedTitle
 *  6  | In-progress customers            | VAS_136_InProgressTitle
 *  7  | No owner                         | VAS_136_NoOwner
 *  8  | Nothing here right now.          | VAS_136_NothingHere
 *  9  | Unable to load onboarding status.| VAS_136_UnableToLoad
 * 10  | Retry                            | VAS_136_Retry
 * 11  | of                               | VAS_136_Of
 * 12  | Close                            | VAS_136_Close
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var CUSTOMER_WINDOW_NAME = 'Business Partner';
    var RING_RADIUS = 42;
    var RING_CIRC = 2 * Math.PI * RING_RADIUS;   // ≈ 263.89

    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }
        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }
        var write = function () { document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px'); };
        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_136_OnboardingStatusWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas136-root">');
        var $body;
        var $sub;

        var $list, $listBody, $listPager, $listTitle;
        var listStatus = 'incomplete';
        var listOffset = 0, listTotal = 0, listSeq = 0;
        var LIST_PAGE = 7;

        // Customer detail popup (reuses the generic VAS_126 endpoint).
        var CUSTOMER_ENDPOINT = 'VAS_126_OpenTicketsWidget/';
        var $detail, $detailBody, $detailSummary, currentDetailId = 0;
        var searchCurrency = { symbol: '', iso: '', precision: 0 };

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
        function icon(name) {
            if (name === 'rocket') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.71-.84.7-2.13-.09-2.91a2.18 2.18 0 0 0-2.91-.09z"></path><path d="m12 15-3-3a22 22 0 0 1 2-3.95A12.88 12.88 0 0 1 22 2c0 2.72-.78 7.5-6 11a22.35 22.35 0 0 1-4 2z"></path><path d="M9 12H4s.55-3.03 2-4c1.62-1.08 5 0 5 0"></path></svg>';
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
            if (name === 'arrow') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>';
            }
            if (name === 'ticket') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v1a2 2 0 0 0 0 4v1a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-1a2 2 0 0 0 0-4z"></path><line x1="9" y1="7" x2="9" y2="17"></line></svg>';
            }
            if (name === 'cash') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"></rect><circle cx="12" cy="12" r="2"></circle><path d="M6 12h.01M18 12h.01"></path></svg>';
            }
            return '';
        }

        var AVATAR_COLORS = ['#1F83FF', '#5F4AA6', '#0B6B45', '#D78B10', '#0083DA', '#A33F3F'];
        function avatarColor(text) {
            var hash = 0, value = String(text || '');
            for (var i = 0; i < value.length; i++) { hash = (hash * 31 + value.charCodeAt(i)) % AVATAR_COLORS.length; }
            return AVATAR_COLORS[hash];
        }
        function initials(name) {
            return String(name || '').split(' ').slice(0, 2).map(function (w) { return w.charAt(0); }).join('').toUpperCase();
        }
        function tierTagHtml(tier) {
            if (!tier) { return ''; }
            var cls = 'vas136-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas136-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas136-tag-warn'; }
            else if (tier === 'Silver') { cls = 'vas136-tag-info'; }
            return '<span class="vas136-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }
        function usesIndian(iso) { return ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'].indexOf(String(iso || '').toUpperCase()) >= 0; }
        function formatArr(value) {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '', abs = Math.abs(n);
            var symbol = searchCurrency.symbol || searchCurrency.iso || '';
            var body = abs >= 1000 ? Math.round(abs / 1000).toLocaleString(window.navigator.language) + 'K' : abs.toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
            return sign + symbol + body;
        }

        function stepClass(step) {
            if (step === 'Complete') { return 'vas136-tag-ok'; }
            if (step === 'Final review') { return 'vas136-tag-info'; }
            if (step === 'Profile setup') { return 'vas136-tag-warn'; }
            return 'vas136-tag-neutral';
        }

        /* ---------- Donut ---------- */

        function loadSummary() {
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_136_OnboardingStatusWidget/GetSummary',
                type: 'GET', cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { renderError(); return; }
                    renderDonut(data || {});
                },
                error: renderError
            });
        }

        function renderState(message) {
            $body.html('<div class="vas136-state">' + escapeHtml(message) + '</div>');
        }
        function renderError() {
            $body.html('<div class="vas136-state">' + escapeHtml(label('VAS_136_UnableToLoad', 'Unable to load onboarding status.')) +
                ' <button type="button" class="vas136-retry">' + escapeHtml(label('VAS_136_Retry', 'Retry')) + '</button></div>');
        }

        function renderDonut(data) {
            var total = Number(data.totalCustomers || 0);
            if (total <= 0) {
                if ($sub) { $sub.text('0% ' + label('VAS_136_Complete', 'complete')); }
                renderState(label('VAS_136_NothingHere', 'Nothing here right now.'));
                return;
            }

            var onboarded = Number(data.onboardedCount || 0);
            var inProgress = Number(data.inProgressCount || 0);
            var pct = Number(data.onboardedPercent || 0);
            if (!isFinite(pct) || pct < 0) { pct = 0; }
            if (pct > 100) { pct = 100; }
            if ($sub) { $sub.text(Math.round(pct) + '% ' + label('VAS_136_Complete', 'complete')); }
            // Green arc length = onboarded share; the amber base ring shows the rest.
            var dashOffset = RING_CIRC * (1 - pct / 100);

            var ring = '<div class="vas136-ring">' +
                '<svg viewBox="0 0 96 96" width="96" height="96">' +
                    '<circle class="vas136-ring-base" cx="48" cy="48" r="' + RING_RADIUS + '" fill="none" stroke-width="10"></circle>' +
                    '<circle class="vas136-ring-val" cx="48" cy="48" r="' + RING_RADIUS + '" fill="none" stroke-width="10" stroke-linecap="round"' +
                        ' stroke-dasharray="' + RING_CIRC.toFixed(2) + '" stroke-dashoffset="' + dashOffset.toFixed(2) + '"></circle>' +
                '</svg>' +
                '<div class="vas136-ring-mid"><b>' + Math.round(pct) + '%</b><span>' + escapeHtml(label('VAS_136_Complete', 'complete')) + '</span></div>' +
            '</div>';

            var legend = '<div class="vas136-legend">' +
                '<button type="button" class="vas136-leg" data-status="done">' +
                    '<span class="vas136-dot vas136-dot-ok"></span>' +
                    '<span class="vas136-leg-lab">' + escapeHtml(label('VAS_136_Onboarded', 'Onboarded')) + '</span>' +
                    '<span class="vas136-leg-num vas136-num-ok">' + formatCount(onboarded) + '</span>' +
                '</button>' +
                '<button type="button" class="vas136-leg" data-status="incomplete">' +
                    '<span class="vas136-dot vas136-dot-warn"></span>' +
                    '<span class="vas136-leg-lab">' + escapeHtml(label('VAS_136_InProgress', 'In progress')) + '</span>' +
                    '<span class="vas136-leg-num vas136-num-warn">' + formatCount(inProgress) + '</span>' +
                '</button>' +
                '<div class="vas136-hint">' + escapeHtml(label('VAS_136_ClickHint', 'Click a segment to work the list')) + '</div>' +
            '</div>';

            $body.html('<div class="vas136-wrap">' + ring + legend + '</div>');
        }

        /* ---------- Drill-down list modal ---------- */

        function openList(status) {
            listStatus = status;
            listOffset = 0;
            $listTitle.text(status === 'done'
                ? label('VAS_136_OnboardedTitle', 'Onboarded customers')
                : label('VAS_136_InProgressTitle', 'In-progress customers'));
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas136-modal-open');
            loadList();
        }
        function closeList() {
            if (!$list) { return; }
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            $('body').removeClass('vas136-modal-open');
        }
        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas136-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_136_OnboardingStatusWidget/GetList',
                type: 'GET', cache: false,
                data: { status: listStatus, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $listBody.html('<div class="vas136-state">' + escapeHtml(label('VAS_136_UnableToLoad', 'Unable to load onboarding status.')) + '</div>'); return; }
                    listTotal = Number(data.total || 0);
                    renderList(data.items || []);
                },
                error: function () { if (seq === listSeq) { $listBody.html('<div class="vas136-state">' + escapeHtml(label('VAS_136_UnableToLoad', 'Unable to load onboarding status.')) + '</div>'); } }
            });
        }
        function renderList(items) {
            if (!items.length) {
                $listBody.html('<div class="vas136-state">' + escapeHtml(label('VAS_136_NothingHere', 'Nothing here right now.')) + '</div>');
                $listPager.empty();
                return;
            }
            var noOwner = label('VAS_136_NoOwner', 'No owner');
            var rows = items.map(function (c) {
                var meta = [c.contact, c.rep || noOwner].filter(function (p) { return p; }).join(' · ');
                var progress = Number(c.onbProgress || 0);
                return '<button type="button" class="vas136-row" data-id="' + Number(c.id) + '">' +
                    '<span class="vas136-row-main">' +
                        '<span class="vas136-row-title" title="' + escapeHtml(c.name) + '">' + escapeHtml(c.name) + '</span>' +
                        '<span class="vas136-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                    '</span>' +
                    '<span class="vas136-tag ' + stepClass(c.onbStep) + '">' + escapeHtml(c.onbStep) + '</span>' +
                    '<span class="vas136-row-pct">' + progress + '%</span>' +
                '</button>';
            }).join('');

            var start = listOffset + 1;
            var end = listOffset + items.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var lbl = start + '–' + end + ' ' + label('VAS_136_Of', 'of') + ' ' + formatCount(listTotal);

            $listBody.html('<div class="vas136-list">' + rows + '</div>');
            if (pages > 1) {
                $listPager.html(
                    '<button type="button" class="vas136-pgbtn" data-dir="prev" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas136-pglabel">' + escapeHtml(lbl) + '</span>' +
                    '<button type="button" class="vas136-pgbtn" data-dir="next" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>'
                );
            } else {
                $listPager.html('<span></span><span class="vas136-pglabel">' + escapeHtml(lbl) + '</span><span></span>');
            }
        }
        function turnPage(direction) {
            var next = listOffset + (direction === 'next' ? LIST_PAGE : -LIST_PAGE);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            listOffset = next;
            loadList();
        }

        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            closeList();
            try {
                $self.widgetFirevalueChanged({ "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId), "TabLayout": "Y", "TabIndex": "0", "ActionName": CUSTOMER_WINDOW_NAME, "ActionType": "W" });
            } catch (e) { /* best-effort */ }
        }

        function createListDialog() {
            $list = $(
                '<div class="vas136-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas136-scrim" data-list-close></div>' +
                    '<section class="vas136-panel">' +
                        '<header class="vas136-phead"><h2 class="vas136-ptitle"></h2>' +
                            '<button type="button" class="vas136-close" data-list-close aria-label="' + escapeHtml(label('VAS_136_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas136-pbody"></div>' +
                        '<footer class="vas136-pfoot"><div class="vas136-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas136-pbody');
            $listPager = $list.find('.vas136-pager');
            $listTitle = $list.find('.vas136-ptitle');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '.vas136-row', function () { openCustomerDetail(Number($(this).attr('data-id'))); });
            $list.on('click', '.vas136-pgbtn', function () { turnPage($(this).attr('data-dir')); });
        }

        /* ---------- Customer detail popup (reuses VAS_126 endpoint) ---------- */

        function anyModalOpen() {
            return ($list && $list.hasClass('is-open')) || ($detail && $detail.hasClass('is-open'));
        }

        function openCustomerDetail(bpId) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas136-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas136-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas136-state">' + escapeHtml(label('VAS_136_UnableToLoad', 'Unable to load onboarding status.')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas136-state">' + escapeHtml(label('VAS_136_UnableToLoad', 'Unable to load onboarding status.')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas136-fact"><div class="vas136-fl">' + escapeHtml(fallback) + '</div><div class="vas136-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas136-signal"><span class="vas136-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas136-sig-main"><div class="vas136-sig-name">' + titleHtml + '</div><div class="vas136-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
        }

        function renderCustomerDetail(data) {
            searchCurrency.symbol = data.currency_symbol || '';
            searchCurrency.iso = data.currency_iso || '';
            searchCurrency.precision = data.std_precision;
            var dash = '—';
            var name = data.name || '';
            var sub = [data.contactName, data.contactEmail].filter(function (p) { return p; }).join(' · ');
            var tierLabel = data.tier || data.tierCode || '';
            var summaryParts = [];
            if (tierLabel) { summaryParts.push(tierLabel); }
            if (data.isKeyClient) { summaryParts.push(label('VAS_136_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas136-factgrid">' +
                fact(label('VAS_136_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_136_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_136_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_136_ARR', 'ARR'), escapeHtml(formatArr(data.value))) +
                fact(label('VAS_136_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_136_Projects', 'Projects'), projects > 0 ? escapeHtml(formatCount(projects)) : dash) +
                fact(label('VAS_136_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatArr(pipeline)) : dash) +
                fact(label('VAS_136_Overdue', 'Overdue'), Number(data.overdueAmount || 0) > 0 ? escapeHtml(formatArr(data.overdueAmount)) : dash) +
            '</div>';

            var signals = '';
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_136_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_136_KeyPrioritise', 'Key client — prioritise') : label('VAS_136_OpenRequests', 'Open support requests'));
            }
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatArr(overdue) + ' ' + label('VAS_136_OverdueWord', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_136_DaysPastDue', 'days past due'));
            }
            var signalsBlock = signals ? '<div class="vas136-signals"><div class="vas136-sig-title">' + escapeHtml(label('VAS_136_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas136-id">' +
                '<span class="vas136-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas136-id-main"><div class="vas136-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas136-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas136-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas136-modal-open'); }
        }

        function createDetailDialog() {
            $detail = $(
                '<div class="vas136-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_136_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas136-scrim" data-detail-close></div>' +
                    '<section class="vas136-dpanel">' +
                        '<header class="vas136-phead"><h2 class="vas136-ptitle">' + escapeHtml(label('VAS_136_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas136-dhead-right"><span class="vas136-dsummary"></span>' +
                                '<button type="button" class="vas136-close" data-detail-close aria-label="' + escapeHtml(label('VAS_136_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas136-dbody"></div>' +
                        '<footer class="vas136-dfoot">' +
                            '<button type="button" class="vas136-btn vas136-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_136_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas136-dbody');
            $detailSummary = $detail.find('.vas136-dsummary');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-act="open"]', function () { var id = currentDetailId; closeDetail(); zoomToCustomer(id); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas136-card">' +
                    '<div class="vas136-head">' +
                        '<span class="vas136-iconwell">' + icon('rocket') + '</span>' +
                        '<div class="vas136-head-txt">' +
                            '<div class="vas136-title">' + escapeHtml(label('VAS_136_OnboardingStatus', 'Onboarding status')) + '</div>' +
                            '<div class="vas136-sub"></div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas136-body"></div>' +
                '</div>'
            );
            $body = $card.find('.vas136-body');
            $sub = $card.find('.vas136-sub');
            $card.on('click', '.vas136-leg', function () { openList($(this).attr('data-status')); });
            $card.on('click', '.vas136-retry', function () { loadSummary(); });
            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createListDialog();
            createDetailDialog();
            $(document).on('keydown.MPCvas136', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); }
                else if ($list && $list.hasClass('is-open')) { closeList(); }
            });
            loadSummary();
        };

        this.refreshWidget = function () { loadSummary(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas136');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            $('body').removeClass('vas136-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_136_OnboardingStatusWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_136_OnboardingStatusWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_136_OnboardingStatusWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_136_OnboardingStatusWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_136_OnboardingStatusWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_136_OnboardingStatusWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
