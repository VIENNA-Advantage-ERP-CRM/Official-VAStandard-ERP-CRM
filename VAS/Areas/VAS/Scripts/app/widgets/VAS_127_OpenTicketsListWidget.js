/**
 * VAS_127 Open Tickets List Widget (Customers module dashboard)
 * Purpose - 3x2 glass triage list: customers with open support tickets, key
 *           clients ranked first then by open-ticket count. Header shows
 *           "{openTicketCount} open · {keyClientCount} key clients affected" and an
 *           "All ->" link that opens the full server-paged list with search. Each
 *           row shows the customer, tier/category, owner and an open-ticket count
 *           tag (red at 3+, amber otherwise) with a View action. Row / View opens
 *           the customer detail modal.
 * Design  - open-tickets.html (attached) + Design Specs/dashboard-widgets.md
 *           "Grid Data Widget". Glass surface, icon-well header, paged rows (7),
 *           circular pager. Internal sizing in em against the widget-root clamp;
 *           borders/radii in px. CSS namespaced vas127-* (MPC prefix rule).
 *
 * Backend - VAS_127_OpenTicketsListWidget/GetSummary
 *           VAS_127_OpenTicketsListWidget/GetRows        (paged ranked rows + search)
 *           Customer detail + activity log reuse the generic VAS_126 endpoints:
 *             VAS_126_OpenTicketsWidget/GetCustomerDetail
 *             VAS_126_OpenTicketsWidget/SaveActivityLog
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                       | Message Key
 * ----+------------------------------------+--------------------------------
 *  1  | Open tickets                       | VAS_127_OpenTickets
 *  2  | open                               | VAS_127_Open
 *  3  | key clients affected               | VAS_127_KeyClientsAffected
 *  4  | key client affected                | VAS_127_KeyClientAffected
 *  5  | All                                | VAS_127_All
 *  6  | View                               | VAS_127_View
 *  7  | Key                                | VAS_127_Key
 *  8  | No owner                           | VAS_127_NoOwner
 *  9  | Nothing here right now.            | VAS_127_NothingHere
 * 10  | Support tickets are not configured.| VAS_127_NotConfigured
 * 11  | Unable to load                     | VAS_127_UnableToLoad
 * 12  | Retry                              | VAS_127_Retry
 * 13  | of                                 | VAS_127_Of
 * 14  | Customers with open tickets        | VAS_127_AllTitle
 * 15  | Search customers…                  | VAS_127_SearchPlaceholder
 * 16  | Close                              | VAS_127_Close
 * 17  | Customer details                   | VAS_127_CustomerDetails
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Generic customer detail / activity-log endpoints (built with VAS_126).
    var CUSTOMER_ENDPOINT = 'VAS_126_OpenTicketsWidget/';
    var CUSTOMER_WINDOW_NAME = 'Business Partner';

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

    var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

    VAS.VAS_127_OpenTicketsListWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas127-root">');
        var $sub;
        var $body;

        var pageSize = 5;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;
        var configured = true;

        // All-list modal state.
        var $all, $allBody, $allPager, $allSearch, $allCount;
        var allOffset = 0, allTotal = 0, allSeq = 0, allSearchTimer = null;
        var ALL_PAGE = 25;

        // Detail + log modal state (reuses VAS_126 generic endpoints).
        var $detail, $detailBody, $detailSummary, currentDetailId = 0, currentDetailName = '';
        var $log, $logCust, $logText, $logError, currentLogBpId = 0, currentLogType = 'Call';
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
            if (name === 'ticket') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v1a2 2 0 0 0 0 4v1a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-1a2 2 0 0 0 0-4z"></path><line x1="9" y1="7" x2="9" y2="17"></line></svg>';
            }
            if (name === 'cash') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"></rect><circle cx="12" cy="12" r="2"></circle><path d="M6 12h.01M18 12h.01"></path></svg>';
            }
            if (name === 'phone') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"></path></svg>';
            }
            if (name === 'check') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';
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
            if (name === 'close') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>';
            }
            if (name === 'search') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="11" cy="11" r="8"></circle><path d="m21 21-4.35-4.35"></path></svg>';
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

        // Tier/category tag: known tier names -> tier colour; any other category ->
        // neutral tag (never discarded).
        function tierTagHtml(tier) {
            if (!tier) { return ''; }
            var cls = 'vas127-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas127-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas127-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas127-tag-info'; }
            return '<span class="vas127-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }

        /* ---------- Summary header ---------- */

        function loadSummary() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_127_OpenTicketsListWidget/GetSummary',
                type: 'GET', cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { return; }
                    if (data.configured === false) { $sub.text(label('VAS_127_NotConfigured', 'Support tickets are not configured.')); return; }
                    var open = Number(data.openTicketCount || 0);
                    var key = Number(data.keyClientCount || 0);
                    var keyWord = key === 1 ? label('VAS_127_KeyClientAffected', 'key client affected') : label('VAS_127_KeyClientsAffected', 'key clients affected');
                    $sub.text(formatCount(open) + ' ' + label('VAS_127_Open', 'open') + ' · ' + formatCount(key) + ' ' + keyWord);
                },
                error: function () { /* header subtitle stays blank on summary error */ }
            });
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_127_OpenTicketsListWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    if (data.configured === false) { configured = false; renderState(label('VAS_127_NotConfigured', 'Support tickets are not configured.')); return; }
                    configured = true;
                    listTotal = Number(data.total || 0);
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) {
            $body.html('<div class="vas127-state">' + escapeHtml(message) + '</div>');
        }

        function renderErrorState() {
            $body.html('<div class="vas127-state">' + escapeHtml(label('VAS_127_UnableToLoad', 'Unable to load')) +
                ' <button type="button" class="vas127-retry">' + escapeHtml(label('VAS_127_Retry', 'Retry')) + '</button></div>');
        }

        function rowHtml(customer) {
            var count = Number(customer.openTicketCount || 0);
            var countCls = count >= 3 ? 'vas127-tag-delayed' : 'vas127-tag-warn';
            var meta = [customer.tier, customer.ownerName || label('VAS_127_NoOwner', 'No owner')]
                .filter(function (p) { return p; }).join(' · ');
            var keyTag = customer.isKeyClient ? '<span class="vas127-tag vas127-tag-key">' + escapeHtml(label('VAS_127_Key', 'Key')) + '</span>' : '';
            return '<div class="vas127-row" data-id="' + Number(customer.customerId) + '">' +
                '<span class="vas127-row-ic">' + icon('ticket') + '</span>' +
                '<span class="vas127-row-main">' +
                    '<span class="vas127-row-title" title="' + escapeHtml(customer.customerName) + '">' + escapeHtml(customer.customerName) + keyTag + '</span>' +
                    '<span class="vas127-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas127-tag ' + countCls + '">' + formatCount(count) + ' ' + escapeHtml(label('VAS_127_Open', 'open')) + '</span>' +
                '<button type="button" class="vas127-view" data-id="' + Number(customer.customerId) + '">' + escapeHtml(label('VAS_127_View', 'View')) + '</button>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) {
                renderState(label('VAS_127_NothingHere', 'Nothing here right now.'));
                return;
            }
            var rows = items.map(rowHtml).join('');
            var start = pageOffset + 1;
            var end = pageOffset + items.length;
            var pages = Math.max(1, Math.ceil(listTotal / pageSize));
            var current = Math.floor(pageOffset / pageSize);
            var lbl = start + '–' + end + ' ' + label('VAS_127_Of', 'of') + ' ' + formatCount(listTotal);

            // Always show the footer label (matches the reference pagedList); the
            // prev/next controls appear only when there is more than one page.
            var pager;
            if (pages > 1) {
                pager = '<div class="vas127-pager">' +
                    '<button type="button" class="vas127-pgbtn" data-dir="prev" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas127-pglabel">' + escapeHtml(lbl) + '</span>' +
                    '<button type="button" class="vas127-pgbtn" data-dir="next" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</div>';
            } else {
                pager = '<div class="vas127-pager"><span></span><span class="vas127-pglabel">' + escapeHtml(lbl) + '</span><span></span></div>';
            }
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas127-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + rows + '</div>' + pager);
        }

        function turnPage(direction) {
            var next = pageOffset + (direction === 'next' ? pageSize : -pageSize);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            pageOffset = next;
            loadRows();
        }

        /* ---------- All (full list) modal ---------- */

        function openAll() {
            allOffset = 0;
            if ($allSearch) { $allSearch.val(''); }
            $all.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas127-modal-open');
            loadAll();
        }
        function closeAll() {
            if (!$all) { return; }
            $all.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas127-modal-open'); }
        }
        function loadAll() {
            var seq = ++allSeq;
            $allBody.html('<div class="vas127-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $allPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_127_OpenTicketsListWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: allOffset, limit: ALL_PAGE, search: ($allSearch.val() || '').trim() },
                success: function (response) {
                    if (seq !== allSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $allBody.html('<div class="vas127-state">' + escapeHtml(label('VAS_127_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    allTotal = Number(data.total || 0);
                    if ($allCount) { $allCount.text(formatCount(allTotal)); }
                    renderAll(data.items || []);
                },
                error: function () { if (seq === allSeq) { $allBody.html('<div class="vas127-state">' + escapeHtml(label('VAS_127_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }
        function renderAll(items) {
            if (!items.length) {
                $allBody.html('<div class="vas127-state">' + escapeHtml(label('VAS_127_NothingHere', 'Nothing here right now.')) + '</div>');
                $allPager.empty();
                return;
            }
            $allBody.html('<div class="vas127-list">' + items.map(rowHtml).join('') + '</div>');
            var start = allOffset + 1, end = allOffset + items.length;
            var pages = Math.max(1, Math.ceil(allTotal / ALL_PAGE));
            var current = Math.floor(allOffset / ALL_PAGE);
            $allPager.html(
                '<button type="button" class="vas127-pgbtn" data-alldir="prev" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                '<span class="vas127-pglabel">' + start + '–' + end + ' ' + escapeHtml(label('VAS_127_Of', 'of')) + ' ' + formatCount(allTotal) + '</span>' +
                '<button type="button" class="vas127-pgbtn" data-alldir="next" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>'
            );
        }
        function turnAllPage(direction) {
            var next = allOffset + (direction === 'next' ? ALL_PAGE : -ALL_PAGE);
            if (next < 0) { next = 0; }
            if (next >= allTotal) { return; }
            allOffset = next;
            loadAll();
        }

        function createAllDialog() {
            $all = $(
                '<div class="vas127-dialog" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_127_AllTitle', 'Customers with open tickets')) + '">' +
                    '<div class="vas127-scrim" data-all-close></div>' +
                    '<section class="vas127-panel">' +
                        '<header class="vas127-phead">' +
                            '<h2 class="vas127-ptitle">' + escapeHtml(label('VAS_127_AllTitle', 'Customers with open tickets')) + '</h2>' +
                            '<div class="vas127-phead-right"><span class="vas127-pcount"></span>' +
                                '<button type="button" class="vas127-close" data-all-close aria-label="' + escapeHtml(label('VAS_127_Close', 'Close')) + '">' + icon('close') + '</button></div>' +
                        '</header>' +
                        '<div class="vas127-search"><span class="vas127-search-ic">' + icon('search') + '</span>' +
                            '<input type="text" class="vas127-search-in" autocomplete="off" placeholder="' + escapeHtml(label('VAS_127_SearchPlaceholder', 'Search customers…')) + '"></div>' +
                        '<div class="vas127-pbody"></div>' +
                        '<footer class="vas127-pfoot"><div class="vas127-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($all);
            $allBody = $all.find('.vas127-pbody');
            $allPager = $all.find('.vas127-pager');
            $allSearch = $all.find('.vas127-search-in');
            $allCount = $all.find('.vas127-pcount');

            $all.on('click', '[data-all-close]', closeAll);
            $all.on('click', '.vas127-row, .vas127-view', function (e) { e.stopPropagation(); openCustomer(Number($(this).attr('data-id'))); });
            $all.on('click', '.vas127-pgbtn', function () { turnAllPage($(this).attr('data-alldir')); });
            $allSearch.on('input', function () {
                if (allSearchTimer) { clearTimeout(allSearchTimer); }
                allSearchTimer = setTimeout(function () { allOffset = 0; loadAll(); }, 300);
            });
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

        function anyModalOpen() {
            return ($all && $all.hasClass('is-open')) || ($detail && $detail.hasClass('is-open')) || ($log && $log.hasClass('is-open'));
        }

        function usesIndianNumbering(iso) { return INDIAN_NUMBERING_CURRENCIES.indexOf(String(iso || '').toUpperCase()) >= 0; }
        function formatArr(value) {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '', abs = Math.abs(n);
            var symbol = searchCurrency.symbol || searchCurrency.iso || '';
            var body = abs >= 1000 ? Math.round(abs / 1000).toLocaleString(window.navigator.language) + 'K' : abs.toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
            return sign + symbol + body;
        }

        function openCustomer(bpId) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas127-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas127-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas127-state">' + escapeHtml(label('VAS_127_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas127-state">' + escapeHtml(label('VAS_127_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas127-fact"><div class="vas127-fl">' + escapeHtml(fallback) + '</div><div class="vas127-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas127-signal"><span class="vas127-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas127-sig-main"><div class="vas127-sig-name">' + titleHtml + '</div><div class="vas127-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
        }

        function renderCustomerDetail(data) {
            searchCurrency.symbol = data.currency_symbol || '';
            searchCurrency.iso = data.currency_iso || '';
            searchCurrency.precision = data.std_precision;
            var dash = '—';
            var name = data.name || '';
            currentDetailName = name;
            var sub = [data.contactName, data.contactEmail].filter(function (p) { return p; }).join(' · ');
            var tierLabel = data.tier || data.tierCode || '';
            var summaryParts = [];
            if (tierLabel) { summaryParts.push(tierLabel); }
            if (data.isKeyClient) { summaryParts.push(label('VAS_127_Key', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas127-factgrid">' +
                fact(label('VAS_127_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_127_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_127_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_127_ARR', 'ARR'), escapeHtml(formatArr(data.value))) +
                fact(label('VAS_127_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_127_Projects', 'Projects'), projects > 0 ? escapeHtml(formatCount(projects)) : dash) +
                fact(label('VAS_127_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatArr(pipeline)) : dash) +
                fact(label('VAS_127_Onboarding', 'Onboarding'), data.onboarding ? escapeHtml(data.onboarding) : dash) +
            '</div>';

            var signals = '';
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_127_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_127_KeyPrioritise', 'Key client — prioritise') : label('VAS_127_OpenRequests', 'Open support requests'));
            }
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatArr(overdue) + ' ' + label('VAS_127_Overdue', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_127_DaysPastDue', 'days past due'));
            }
            var signalsBlock = signals ? '<div class="vas127-signals"><div class="vas127-sig-title">' + escapeHtml(label('VAS_127_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas127-id">' +
                '<span class="vas127-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas127-id-main"><div class="vas127-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas127-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas127-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas127-modal-open'); }
        }

        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            closeDetail();
            try {
                $self.widgetFirevalueChanged({ "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId), "TabLayout": "Y", "TabIndex": "0", "ActionName": CUSTOMER_WINDOW_NAME, "ActionType": "W" });
            } catch (e) { /* best-effort */ }
        }

        function createDetailDialog() {
            $detail = $(
                '<div class="vas127-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_127_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas127-scrim" data-detail-close></div>' +
                    '<section class="vas127-dpanel">' +
                        '<header class="vas127-phead"><h2 class="vas127-ptitle">' + escapeHtml(label('VAS_127_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas127-phead-right"><span class="vas127-dsummary"></span>' +
                                '<button type="button" class="vas127-close" data-detail-close aria-label="' + escapeHtml(label('VAS_127_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas127-dbody"></div>' +
                        '<footer class="vas127-dfoot">' +
                            '<button type="button" class="vas127-btn vas127-btn-ghost" data-detail-act="log">' + icon('phone') + escapeHtml(label('VAS_127_Log', 'Log')) + '</button>' +
                            '<button type="button" class="vas127-btn vas127-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_127_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas127-dbody');
            $detailSummary = $detail.find('.vas127-dsummary');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-act="open"]', function () { var id = currentDetailId; closeDetail(); zoomToCustomer(id); });
            $detail.on('click', '[data-detail-act="log"]', function () { openLogDialog(currentDetailId, currentDetailName); });
        }

        /* ---------- Log activity popup (reuses VAS_126 SaveActivityLog) ---------- */

        function openLogDialog(bpId, name) {
            currentLogBpId = bpId;
            currentLogType = 'Call';
            $log.find('.vas127-ltype').removeClass('is-active').filter('[data-type="Call"]').addClass('is-active');
            $logCust.text(name || '');
            $logText.val('');
            $logError.hide().text('');
            $log.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas127-modal-open');
            window.setTimeout(function () { $logText.focus(); }, 0);
        }
        function closeLog() {
            if (!$log) { return; }
            if (document.activeElement && $log[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $log.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas127-modal-open'); }
        }
        function saveLog() {
            var summary = ($logText.val() || '').trim();
            if (!summary) { $logError.text(label('VAS_127_SummaryRequired', 'Please enter a summary.')).show(); return; }
            var $save = $log.find('[data-log-save]');
            $save.prop('disabled', true);
            $logError.hide().text('');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'SaveActivityLog',
                type: 'POST', data: { C_BPartner_ID: currentLogBpId, activityType: currentLogType, summary: summary },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $logError.text(data.error).show(); $save.prop('disabled', false); return; }
                    $save.prop('disabled', false); closeLog();
                },
                error: function () { $logError.text(label('VAS_127_LogSaveFailed', 'Could not save the activity.')).show(); $save.prop('disabled', false); }
            });
        }
        function createLogDialog() {
            var types = ['Call', 'Note', 'Meeting', 'Email'].map(function (t) {
                return '<button type="button" class="vas127-ltype' + (t === 'Call' ? ' is-active' : '') + '" data-type="' + t + '">' + escapeHtml(label('VAS_127_Type' + t, t)) + '</button>';
            }).join('');
            $log = $(
                '<div class="vas127-log" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_127_LogActivity', 'Log activity')) + '">' +
                    '<div class="vas127-scrim" data-log-close></div>' +
                    '<section class="vas127-lpanel">' +
                        '<header class="vas127-phead"><h2 class="vas127-ptitle">' + escapeHtml(label('VAS_127_LogActivity', 'Log activity')) + '</h2>' +
                            '<div class="vas127-phead-right"><span class="vas127-lcust"></span>' +
                                '<button type="button" class="vas127-close" data-log-close aria-label="' + escapeHtml(label('VAS_127_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas127-lbody"><div class="vas127-ltypes">' + types + '</div>' +
                            '<label class="vas127-llabel">' + escapeHtml(label('VAS_127_Summary', 'Summary')) + '</label>' +
                            '<textarea class="vas127-ltext" placeholder="' + escapeHtml(label('VAS_127_SummaryPlaceholder2', 'What happened / next step…')) + '"></textarea>' +
                            '<div class="vas127-lerror"></div></div>' +
                        '<footer class="vas127-dfoot">' +
                            '<button type="button" class="vas127-btn vas127-btn-ghost" data-log-close>' + escapeHtml(label('VAS_127_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas127-btn vas127-btn-primary" data-log-save>' + icon('check') + escapeHtml(label('VAS_127_Save', 'Save')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($log);
            $logCust = $log.find('.vas127-lcust');
            $logText = $log.find('.vas127-ltext');
            $logError = $log.find('.vas127-lerror');
            $logError.hide();
            $log.on('click', '[data-log-close]', closeLog);
            $log.on('click', '.vas127-ltype', function () { currentLogType = $(this).attr('data-type'); $log.find('.vas127-ltype').removeClass('is-active'); $(this).addClass('is-active'); });
            $log.on('click', '[data-log-save]', saveLog);
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas127-card">' +
                    '<div class="vas127-head">' +
                        '<div class="vas127-head-l">' +
                            '<span class="vas127-iconwell">' + icon('ticket') + '</span>' +
                            '<div class="vas127-head-txt"><div class="vas127-title">' + escapeHtml(label('VAS_127_OpenTickets', 'Open tickets')) + '</div>' +
                                '<div class="vas127-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas127-alllink">' + escapeHtml(label('VAS_127_All', 'All')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas127-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas127-sub');
            $body = $card.find('.vas127-body');

            $card.on('click', '.vas127-alllink', function () { openAll(); });
            $card.on('click', '.vas127-view', function (e) { e.stopPropagation(); openCustomer(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas127-row', function () { openCustomer(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas127-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $card.on('click', '.vas127-retry', function () { loadSummary(); loadRows(); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createAllDialog();
            createDetailDialog();
            createLogDialog();

            $(document).on('keydown.MPCvas127', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($log && $log.hasClass('is-open')) { closeLog(); }
                else if ($detail && $detail.hasClass('is-open')) { closeDetail(); }
                else if ($all && $all.hasClass('is-open')) { closeAll(); }
            });

            loadSummary();
            loadRows();
        };

        this.refreshWidget = function () {
            pageOffset = 0;
            loadSummary();
            loadRows();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas127');
            if (allSearchTimer) { clearTimeout(allSearchTimer); }
            if ($all) { $all.remove(); $all = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($log) { $log.remove(); $log = null; }
            $('body').removeClass('vas127-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_127_OpenTicketsListWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_127_OpenTicketsListWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };
    VAS.VAS_127_OpenTicketsListWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_127_OpenTicketsListWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_127_OpenTicketsListWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_127_OpenTicketsListWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
