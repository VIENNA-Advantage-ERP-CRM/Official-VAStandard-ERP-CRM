/**
 * VAS_296 Open Proposals Widget (Customers module dashboard)
 * Purpose - 3x2 glass list: customers with open sales proposals, ranked by
 *           proposal value desc. Each row shows the customer, the proposal
 *           count and owner, and the value (violet - pairs with the Sales
 *           Proposal module) with a "View" action. Header shows
 *           "{n} clients · {value} open" and an "All ->" link that opens the
 *           full server-paged list. Row / View opens the customer detail
 *           modal (shared with VAS_126/138/295).
 * Design  - open-proposals.html (attached) + Design Specs/dashboard-widgets.md
 *           "Grid Data Widget". Glass surface, icon-well header, ranked rows
 *           (paged 7), circular pager. Internal sizing in em against the
 *           widget-root clamp; borders/radii in px. CSS namespaced vas296-*
 *           (MPC prefix rule).
 *
 * Backend - VAS_296_OpenProposalsWidget/GetRows                    (paged ranked rows)
 *           Customer detail reuses the generic VAS_126 endpoint:
 *             VAS_126_OpenTicketsWidget/GetCustomerDetail
 *           Projects modal reuses VAS_135's endpoints (same pattern VAS_138/295
 *           already use):
 *             VAS_135_ActiveProjectsWidget/GetProjects
 *             VAS_135_ActiveProjectsWidget/GetActivityDetail
 *
 * Routing - hosted on a window, opening the customer navigates the host grid
 *           in place (widgetFirevalueChanged); on the Home / landing dashboard
 *           (windowNo < 0) there is no host grid, so the record is opened in
 *           the standard Customer window via VAS.ZoomUtil. Matches VAS_135/138/295.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+----------------------------------+--------------------------------
 *  1  | Open proposals                  | VAS_296_OpenProposals
 *  2  | clients                         | VAS_296_Clients
 *  3  | client                          | VAS_296_Client
 *  4  | open                            | VAS_296_Open2
 *  5  | proposal                        | VAS_296_Proposal
 *  6  | proposals                       | VAS_296_Proposals
 *  7  | All                             | VAS_296_All
 *  8  | View                            | VAS_296_View
 *  9  | No owner                        | VAS_296_NoOwner
 * 10  | Nothing here right now.         | VAS_296_NothingHere
 * 11  | Unable to load                  | VAS_296_UnableToLoad
 * 12  | of                              | VAS_296_Of
 * 13  | Showing                         | VAS_296_Showing
 * 14  | Previous page                   | VAS_296_PrevPage
 * 15  | Next page                       | VAS_296_NextPage
 * 16  | Customers with open proposals   | VAS_296_AllTitle
 * 17  | Close                           | VAS_296_Close
 * 18  | Customer details                | VAS_296_CustomerDetails
 * 19  | Tier                            | VAS_296_Tier
 * 20  | Segment                         | VAS_296_Segment
 * 21  | Owner                           | VAS_296_Owner
 * 22  | ARR                             | VAS_296_ARR
 * 23  | Open tickets                    | VAS_296_OpenTicketsFact
 * 24  | Projects                        | VAS_296_Projects
 * 25  | Pipeline                        | VAS_296_Pipeline
 * 26  | Onboarding                      | VAS_296_Onboarding
 * 27  | Key client                      | VAS_296_KeyClient
 * 28  | Signals                         | VAS_296_Signals
 * 29  | overdue                         | VAS_296_Overdue
 * 30  | days past due                   | VAS_296_DaysPastDue
 * 31  | open support tickets            | VAS_296_OpenSupportTickets
 * 32  | Key client — prioritise         | VAS_296_KeyPrioritise
 * 33  | Open support requests           | VAS_296_OpenRequests
 * 34  | Open record                     | VAS_296_OpenRecord
 * 35  | Active projects                 | VAS_296_ActiveProjects
 * 36  | Delayed                         | VAS_296_StateDelayed
 * 37  | On time                         | VAS_296_StateOnTime
 * 38  | Ongoing                         | VAS_296_StateOngoing
 * 39  | Upcoming                        | VAS_296_StateUpcoming
 * 40  | Completed                       | VAS_296_StateCompleted
 * 41  | Due                             | VAS_296_Due
 * 42  | activities                      | VAS_296_Activities
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Generic customer detail endpoint (built with VAS_126).
    var CUSTOMER_ENDPOINT = 'VAS_126_OpenTicketsWidget/';
    // Customer projects endpoints (built with VAS_135).
    var PROJECT_ENDPOINT = 'VAS_135_ActiveProjectsWidget/';
    var CUSTOMER_WINDOW_NAME = 'Business Partner';

    /* Zoom target when the widget is NOT hosted inside a window (windowNo < 0 -
       the Home / landing dashboard). There is no host grid to navigate there, so
       the record is opened in the standard Customer window; VAS.ZoomUtil
       resolves the AD_Window_ID from the new name, then the old name, then
       VAS_ZoomScreenConfig. */
    var ZOOM_WINDOW_NAME_NEW = 'VAS_CustomerMaster';
    var ZOOM_WINDOW_NAME_OLD = CUSTOMER_WINDOW_NAME;

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

    VAS.VAS_296_OpenProposalsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas296-root">');
        var $sub;
        var $body;

        var pageSize = 7;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;
        var widgetCurrency = { symbol: '', iso: '', precision: 2 };

        // All-list modal state.
        var $all, $allBody, $allPager, $allCount;
        var allOffset = 0, allTotal = 0, allSeq = 0;
        var ALL_PAGE = 25;

        // Detail modal state (reuses VAS_126 generic endpoints).
        var $detail, $detailBody, $detailSummary, currentDetailId = 0, currentDetailName = '';
        var detailCurrency = { symbol: '', iso: '', precision: 2 };

        // Projects modal state (reuses VAS_135 project endpoints).
        var $proj, $projBody, $projCust, currentProjId = 0, activeProjState = null;
        var PROJECT_STATES = ['delayed', 'ontime', 'ongoing', 'upcoming', 'completed'];

        // AD_Window_ID of the Customer window, resolved once on the first Home-page
        // zoom and reused afterwards (0 = not resolved yet).
        var zoomWindowId = 0;

        function label(key, fallback) {
            return VIS.Msg.getMsg(key);
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
        function formatDate(iso) {
            if (!iso) { return '—'; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return iso; }
            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }

        // Standard precision of the supplied base-currency descriptor, falling back to
        // the session context when the endpoint did not send one.
        function precisionOf(cur) {
            var p = Number(cur && cur.precision);
            if (!isNaN(p) && p >= 0) { return p; }
            if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                p = Number(VIS.Env.getCtx().getStdPrecision());
            }
            return !isNaN(p) && p >= 0 ? p : 0;
        }

        // Compact money against the base (accounting-schema) currency the
        // endpoint reported.
        function formatMoney(value, cur) {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '';
            var iso = (cur && cur.iso) || '';
            var symbol = (cur && (cur.symbol || cur.iso)) || '';
            return sign + symbol + VIS.Util.formatCompactAmount(n, iso, precisionOf(cur));
        }
        // Projects fact: name the customer's delivery project rather than only
        // counting it (matches VAS_138/295).
        function projectText(name, count, dash) {
            if (!name) { return dash; }
            var extra = Number(count || 0) - 1;
            return escapeHtml(extra > 0 ? name + ' +' + formatCount(extra) : name);
        }

        function icon(name) {
            if (name === 'doc') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><path d="M14 2v6h6"></path></svg>';
            }
            if (name === 'ticket') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v1a2 2 0 0 0 0 4v1a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-1a2 2 0 0 0 0-4z"></path><line x1="9" y1="7" x2="9" y2="17"></line></svg>';
            }
            if (name === 'cash') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"></rect><circle cx="12" cy="12" r="2"></circle><path d="M6 12h.01M18 12h.01"></path></svg>';
            }
            if (name === 'folder') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.93a2 2 0 0 1-1.66-.9l-.82-1.2A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13c0 1.1.9 2 2 2z"></path></svg>';
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
            var cls = 'vas296-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas296-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas296-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas296-tag-info'; }
            return '<span class="vas296-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }
        function stateLabel(state) {
            if (state === 'delayed') { return label('VAS_296_StateDelayed', 'Delayed'); }
            if (state === 'ontime') { return label('VAS_296_StateOnTime', 'On time'); }
            if (state === 'ongoing') { return label('VAS_296_StateOngoing', 'Ongoing'); }
            if (state === 'upcoming') { return label('VAS_296_StateUpcoming', 'Upcoming'); }
            return label('VAS_296_StateCompleted', 'Completed');
        }
        function projStatusClass(status) {
            if (status === 'Delayed') { return 'vas296-tag-delayed'; }
            if (status === 'Ongoing') { return 'vas296-tag-ongoing'; }
            if (status === 'Upcoming') { return 'vas296-tag-upcoming'; }
            return 'vas296-tag-ontime';
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_296_OpenProposalsWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    listTotal = Number(data.total || 0);
                    widgetCurrency = { symbol: data.currency_symbol || widgetCurrency.symbol, iso: data.currency_iso || widgetCurrency.iso, precision: data.std_precision };
                    var clientWord = listTotal === 1 ? label('VAS_296_Client', 'client') : label('VAS_296_Clients', 'clients');
                    $sub.text(formatCount(listTotal) + ' ' + clientWord + ' · ' + formatMoney(data.total_value, widgetCurrency) + ' ' + label('VAS_296_Open2', 'open'));
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) { $body.html('<div class="vas296-state">' + escapeHtml(message) + '</div>'); }
        function renderErrorState() { $body.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_UnableToLoad', 'Unable to load')) + '</div>'); }

        function rowHtml(item, cur) {
            var count = Number(item.proposalCount || 0);
            var propWord = count === 1 ? label('VAS_296_Proposal', 'proposal') : label('VAS_296_Proposals', 'proposals');
            var owner = item.ownerName || label('VAS_296_NoOwner', 'No owner');
            var meta = formatCount(count) + ' ' + propWord + ' · ' + owner;
            return '<div class="vas296-row" data-id="' + Number(item.customerId) + '">' +
                '<span class="vas296-row-ic">' + icon('doc') + '</span>' +
                '<span class="vas296-row-main">' +
                    '<span class="vas296-row-title" title="' + escapeHtml(item.customerName) + '">' + escapeHtml(item.customerName) + '</span>' +
                    '<span class="vas296-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas296-row-amt">' +
                    '<span class="vas296-amt-val">' + escapeHtml(formatMoney(item.proposalValue, cur)) + '</span>' +
                '</span>' +
                '<button type="button" class="vas296-view" data-id="' + Number(item.customerId) + '">' + escapeHtml(label('VAS_296_View', 'View')) + '</button>' +
            '</div>';
        }

        /* Footer pager (Design Specs/dashboard-widgets.md §"Widget Footer Pager"):
           "Showing X–Y of Z" helper on the left, compact prev · "N of M" · next
           control on the right. The control stays visible on a single page with
           both arrows disabled, so the footer height never shifts. */
        function pagerHtml(offset, size, total) {
            var start = offset + 1;
            var pages = Math.max(1, Math.ceil(total / size));
            var current = Math.floor(offset / size);
            var end = Math.min(offset + size, total);
            var of = label('VAS_296_Of', 'of');
            var helper = label('VAS_296_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(total);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;
            return '<div class="vas296-pager">' +
                '<span class="vas296-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas296-pgctl">' +
                    '<button type="button" class="vas296-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_296_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas296-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas296-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_296_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) { renderState(label('VAS_296_NothingHere', 'Nothing here right now.')); return; }
            var cur = widgetCurrency;
            var rows = items.map(function (it) { return rowHtml(it, cur); }).join('');
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas296-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + rows + '</div>' + pagerHtml(pageOffset, pageSize, listTotal));
        }

        function turnPage(direction) {
            var next = pageOffset + (direction === 'next' ? pageSize : -pageSize);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            pageOffset = next;
            loadRows();
        }

        /* ---------- All (full list) modal ---------- */

        function anyModalOpen() {
            return ($all && $all.hasClass('is-open')) || ($detail && $detail.hasClass('is-open')) ||
                ($proj && $proj.hasClass('is-open'));
        }
        function openAll() {
            allOffset = 0;
            $all.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas296-modal-open');
            loadAll();
        }
        function closeAll() {
            if (!$all) { return; }
            $all.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas296-modal-open'); }
        }
        function loadAll() {
            var seq = ++allSeq;
            $allBody.html('<div class="vas296-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $allPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_296_OpenProposalsWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: allOffset, limit: ALL_PAGE },
                success: function (response) {
                    if (seq !== allSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $allBody.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    allTotal = Number(data.total || 0);
                    if ($allCount) { $allCount.text(formatCount(allTotal)); }
                    var cur = { symbol: data.currency_symbol || widgetCurrency.symbol, iso: data.currency_iso || widgetCurrency.iso, precision: data.std_precision };
                    var items = data.items || [];
                    if (!items.length) { $allBody.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_NothingHere', 'Nothing here right now.')) + '</div>'); $allPager.empty(); return; }
                    $allBody.html('<div class="vas296-list">' + items.map(function (it) { return rowHtml(it, cur); }).join('') + '</div>');
                    $allPager.html(pagerHtml(allOffset, ALL_PAGE, allTotal));
                },
                error: function () { if (seq === allSeq) { $allBody.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
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
                '<div class="vas296-dialog" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_296_AllTitle', 'Customers with open proposals')) + '">' +
                    '<div class="vas296-scrim" data-all-close></div>' +
                    '<section class="vas296-panel">' +
                        '<header class="vas296-phead">' +
                            '<h2 class="vas296-ptitle">' + escapeHtml(label('VAS_296_AllTitle', 'Customers with open proposals')) + '</h2>' +
                            '<div class="vas296-phead-right"><span class="vas296-pcount"></span>' +
                                '<button type="button" class="vas296-close" data-all-close aria-label="' + escapeHtml(label('VAS_296_Close', 'Close')) + '">' + icon('close') + '</button></div>' +
                        '</header>' +
                        '<div class="vas296-pbody"></div>' +
                        '<footer class="vas296-pfoot"><div class="vas296-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($all);
            $allBody = $all.find('.vas296-pbody');
            $allPager = $all.find('.vas296-pager');
            $allCount = $all.find('.vas296-pcount');

            $all.on('click', '[data-all-close]', closeAll);
            $all.on('click', '.vas296-row, .vas296-view', function (e) { e.stopPropagation(); openCustomer(Number($(this).attr('data-id'))); });
            $all.on('click', '.vas296-pgbtn', function () { turnAllPage($(this).attr('data-dir')); });
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

        function openCustomer(bpId) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas296-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas296-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas296-fact"><div class="vas296-fl">' + escapeHtml(fallback) + '</div><div class="vas296-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas296-signal"><span class="vas296-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas296-sig-main"><div class="vas296-sig-name">' + titleHtml + '</div><div class="vas296-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
        }

        function renderCustomerDetail(data) {
            detailCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
            var dash = '—';
            var name = data.name || '';
            currentDetailName = name;
            var sub = [data.contactName, data.contactEmail].filter(function (p) { return p; }).join(' · ');
            var tierLabel = data.tier || data.tierCode || '';
            var summaryParts = [];
            if (tierLabel) { summaryParts.push(tierLabel); }
            if (data.isKeyClient) { summaryParts.push(label('VAS_296_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas296-factgrid">' +
                fact(label('VAS_296_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_296_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_296_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_296_ARR', 'ARR'), escapeHtml(formatMoney(data.value, detailCurrency))) +
                fact(label('VAS_296_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_296_Projects', 'Projects'), projectText(data.projectName, projects, dash)) +
                fact(label('VAS_296_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatMoney(pipeline, detailCurrency)) : dash) +
                fact(label('VAS_296_Onboarding', 'Onboarding'), data.onboardingPercent == null ? dash : escapeHtml(formatCount(data.onboardingPercent) + '%')) +
            '</div>';

            var signals = '';
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatMoney(overdue, detailCurrency) + ' ' + label('VAS_296_Overdue', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_296_DaysPastDue', 'days past due'));
            }
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_296_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_296_KeyPrioritise', 'Key client — prioritise') : label('VAS_296_OpenRequests', 'Open support requests'));
            }
            var signalsBlock = signals ? '<div class="vas296-signals"><div class="vas296-sig-title">' + escapeHtml(label('VAS_296_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas296-id">' +
                '<span class="vas296-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas296-id-main"><div class="vas296-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas296-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas296-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas296-modal-open'); }
        }

        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            closeDetail();
            try {
                if ($self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({ "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId), "TabLayout": "Y", "TabIndex": "0", "ActionName": CUSTOMER_WINDOW_NAME, "ActionType": "W" });
                }
                else {
                    /* Home / landing page: no host grid, so open the standard Customer window. */
                    VAS.ZoomUtil.zoomToRecord("C_BPartner_ID", Number(bpId), zoomWindowId, ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                        .done(function (id) {
                            if (id > 0) { zoomWindowId = id; }
                        });
                }
            } catch (e) { /* best-effort */ }
        }

        function createDetailDialog() {
            $detail = $(
                '<div class="vas296-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_296_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas296-scrim" data-detail-close></div>' +
                    '<section class="vas296-dpanel">' +
                        '<header class="vas296-phead"><h2 class="vas296-ptitle">' + escapeHtml(label('VAS_296_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas296-phead-right"><span class="vas296-dsummary"></span>' +
                                '<button type="button" class="vas296-close" data-detail-close aria-label="' + escapeHtml(label('VAS_296_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas296-dbody"></div>' +
                        '<footer class="vas296-dfoot">' +
                            '<button type="button" class="vas296-btn vas296-btn-ghost" data-detail-act="projects">' + icon('folder') + escapeHtml(label('VAS_296_Projects', 'Projects')) + '</button>' +
                            '<button type="button" class="vas296-btn vas296-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_296_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas296-dbody');
            $detailSummary = $detail.find('.vas296-dsummary');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-act="open"]', function () { var id = currentDetailId; closeDetail(); zoomToCustomer(id); });
            $detail.on('click', '[data-detail-act="projects"]', function () { openProjects(currentDetailId, currentDetailName); });
        }

        /* ---------- Projects modal (reuses VAS_135 project endpoints) ---------- */

        function openProjects(bpId, name) {
            if (!bpId) { return; }
            currentProjId = bpId;
            activeProjState = null;
            $projCust.text(name || currentDetailName || '');
            $proj.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas296-modal-open');
            $projBody.html('<div class="vas296-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetProjects',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $projBody.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderProjects(data || {});
                },
                error: function () { $projBody.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }
        function closeProjects() {
            if (!$proj) { return; }
            if (document.activeElement && $proj[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $proj.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas296-modal-open'); }
        }
        function projStatCellHtml(state, count) {
            return '<button type="button" class="vas296-astat MPC-astat-' + state + (activeProjState === state ? ' is-active' : '') + '" data-state="' + state + '">' +
                '<b>' + formatCount(count) + '</b><span>' + escapeHtml(stateLabel(state)) + '</span></button>';
        }
        function projectRowHtml(project) {
            var total = Number(project.total || 0);
            var progress = Number(project.progressPercent || 0);
            var breakdown = PROJECT_STATES.map(function (s) {
                var n = Number(project[s] || 0);
                return n > 0 ? '<span class="vas296-bd MPC-bd-' + s + '">' + formatCount(n) + ' ' + escapeHtml(stateLabel(s)) + '</span>' : '';
            }).filter(function (x) { return x; }).join('');
            return '<div class="vas296-proj">' +
                '<div class="vas296-proj-head">' +
                    '<span class="vas296-proj-name" title="' + escapeHtml(project.name) + '">' + escapeHtml(project.name) + '</span>' +
                    '<span class="vas296-tag ' + projStatusClass(project.status) + '">' + escapeHtml(project.status) + '</span>' +
                '</div>' +
                (breakdown ? '<div class="vas296-proj-bd">' + breakdown + '</div>' : '') +
                '<div class="vas296-bar"><div style="width:' + Math.max(0, Math.min(100, progress)) + '%"></div></div>' +
                '<div class="vas296-proj-foot">' +
                    '<span>' + formatCount(project.completed) + '/' + formatCount(total) + ' ' + escapeHtml(label('VAS_296_Activities', 'activities')) + ' · ' + progress + '%</span>' +
                    '<span>' + escapeHtml(label('VAS_296_Due', 'Due')) + ' ' + escapeHtml(formatDate(project.dueDate)) + '</span>' +
                '</div>' +
            '</div>';
        }
        function renderProjects(data) {
            var summary = data.summary || {};
            var projects = data.projects || [];
            var cells = PROJECT_STATES.map(function (s) { return projStatCellHtml(s, summary[s] || 0); }).join('');
            var projRows = projects.length
                ? projects.map(projectRowHtml).join('')
                : '<div class="vas296-state">' + escapeHtml(label('VAS_296_NothingHere', 'Nothing here right now.')) + '</div>';
            $projBody.html(
                '<div class="vas296-statgrid">' + cells + '</div>' +
                '<div class="vas296-actdetail"></div>' +
                '<div class="vas296-projects">' + projRows + '</div>'
            );
        }
        function toggleProjState(state) {
            var $detailBox = $proj.find('.vas296-actdetail');
            if (activeProjState === state) {
                activeProjState = null;
                $proj.find('.vas296-astat').removeClass('is-active');
                $detailBox.empty();
                return;
            }
            activeProjState = state;
            $proj.find('.vas296-astat').removeClass('is-active').filter('[data-state="' + state + '"]').addClass('is-active');
            $detailBox.html('<div class="vas296-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetActivityDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: currentProjId, state: state },
                success: function (response) {
                    if (activeProjState !== state) { return; }
                    var data = parseResponse(response);
                    var items = (data && data.items) || [];
                    if (!items.length) { $detailBox.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_NothingHere', 'Nothing here right now.')) + '</div>'); return; }
                    var rows = items.map(function (a) {
                        return '<div class="vas296-act">' +
                            '<div class="vas296-act-main"><span class="vas296-act-name" title="' + escapeHtml(a.phaseName) + '">' + escapeHtml(a.phaseName) + '</span>' +
                                '<span class="vas296-act-proj" title="' + escapeHtml(a.projectName) + '">' + escapeHtml(a.projectName) + '</span></div>' +
                            '<span class="vas296-act-due">' + escapeHtml(label('VAS_296_Due', 'Due')) + ' ' + escapeHtml(formatDate(a.dueDate)) + '</span>' +
                        '</div>';
                    }).join('');
                    $detailBox.html('<div class="vas296-actlist">' + rows + '</div>');
                },
                error: function () { if (activeProjState === state) { $detailBox.html('<div class="vas296-state">' + escapeHtml(label('VAS_296_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }
        function createProjectsDialog() {
            $proj = $(
                '<div class="vas296-proj-modal" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_296_ActiveProjects', 'Active projects')) + '">' +
                    '<div class="vas296-scrim" data-proj-close></div>' +
                    '<section class="vas296-ppanel">' +
                        '<header class="vas296-phead"><h2 class="vas296-ptitle">' + escapeHtml(label('VAS_296_ActiveProjects', 'Active projects')) + '</h2>' +
                            '<div class="vas296-phead-right"><span class="vas296-psummary"></span>' +
                                '<button type="button" class="vas296-close" data-proj-close aria-label="' + escapeHtml(label('VAS_296_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas296-projbody"></div>' +
                        '<footer class="vas296-dfoot">' +
                            '<button type="button" class="vas296-btn vas296-btn-ghost" data-proj-close>' + escapeHtml(label('VAS_296_CustomerDetails', 'Customer details')) + '</button>' +
                            '<button type="button" class="vas296-btn vas296-btn-primary" data-proj-act="open">' + icon('arrow') + escapeHtml(label('VAS_296_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($proj);
            $projBody = $proj.find('.vas296-projbody');
            $projCust = $proj.find('.vas296-psummary');
            $proj.on('click', '[data-proj-close]', closeProjects);
            $proj.on('click', '[data-proj-act="open"]', function () { var id = currentProjId; closeProjects(); zoomToCustomer(id); });
            $proj.on('click', '.vas296-astat', function () { toggleProjState($(this).attr('data-state')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas296-card">' +
                    '<div class="vas296-head">' +
                        '<div class="vas296-head-l">' +
                            '<span class="vas296-iconwell">' + icon('doc') + '</span>' +
                            '<div class="vas296-head-txt"><div class="vas296-title">' + escapeHtml(label('VAS_296_OpenProposals', 'Open proposals')) + '</div>' +
                                '<div class="vas296-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas296-alllink">' + escapeHtml(label('VAS_296_All', 'All')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas296-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas296-sub');
            $body = $card.find('.vas296-body');

            $card.on('click', '.vas296-alllink', function () { openAll(); });
            $card.on('click', '.vas296-view', function (e) { e.stopPropagation(); openCustomer(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas296-row', function () { openCustomer(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas296-pgbtn', function () { turnPage($(this).attr('data-dir')); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createAllDialog();
            createDetailDialog();
            createProjectsDialog();
            $(document).on('keydown.MPCvas296', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($proj && $proj.hasClass('is-open')) { closeProjects(); }
                else if ($detail && $detail.hasClass('is-open')) { closeDetail(); }
                else if ($all && $all.hasClass('is-open')) { closeAll(); }
            });
            loadRows();
        };

        this.refreshWidget = function () { pageOffset = 0; loadRows(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas296');
            if ($all) { $all.remove(); $all = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($proj) { $proj.remove(); $proj = null; }
            $('body').removeClass('vas296-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_296_OpenProposalsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_296_OpenProposalsWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_296_OpenProposalsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_296_OpenProposalsWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_296_OpenProposalsWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_296_OpenProposalsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
