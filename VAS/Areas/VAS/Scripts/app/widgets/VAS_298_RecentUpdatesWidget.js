/**
 * VAS_298 Recent Updates Widget (Customers module dashboard)
 * Purpose - 3x2 glass list: customers with a CRM activity logged in the last
 *           12 days, newest first. Each row shows a kind-specific icon/colour
 *           (note/email/call/meeting), the logged text, how long ago, and a
 *           tier tag. Header shows "{n} clients with activity" and an
 *           "All ->" link that opens the full server-paged list. Row click
 *           opens the customer detail modal (shared with VAS_126/138/295/
 *           296/297).
 *
 *           "Logging an activity elsewhere updates a customer's lastUpdateDays
 *           and re-renders" (brief): this widget reads live from R_Request, so
 *           any newly logged activity (via VAS_120's search quick action, or
 *           any future caller of VAS_126/SaveActivityLog) is already the row
 *           on the NEXT refreshWidget() - the standard host refresh cycle
 *           every widget in this dashboard relies on. There is no cross-widget
 *           push/event-bus in this codebase (VAS_120's
 *           VAS.VAS_120_openCustomerActivity is an optional override hook,
 *           never itself defined), so adding one is out of scope for a single
 *           widget; live cross-widget push was not part of the brief either.
 *
 * Design  - recent-updates.html (attached) + Design Specs/dashboard-widgets.md
 *           "Grid Data Widget". Glass surface, icon-well header, ranked rows
 *           (paged 7), circular pager. Internal sizing in em against the
 *           widget-root clamp; borders/radii in px. CSS namespaced vas298-*
 *           (MPC prefix rule).
 *
 * Backend - VAS_298_RecentUpdatesWidget/GetRows                    (paged customer rows + total)
 *           Customer detail reuses the generic VAS_126 endpoint:
 *             VAS_126_OpenTicketsWidget/GetCustomerDetail
 *           Projects modal reuses VAS_135's endpoints (same pattern VAS_138/
 *           295/296/297 already use):
 *             VAS_135_ActiveProjectsWidget/GetProjects
 *             VAS_135_ActiveProjectsWidget/GetActivityDetail
 *
 * Routing - hosted on a window, opening the customer navigates the host grid
 *           in place (widgetFirevalueChanged); on the Home / landing
 *           dashboard (windowNo < 0) there is no host grid, so the record is
 *           opened in the standard Customer window via VAS.ZoomUtil.
 *           Matches VAS_135/138/295/296/297.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+----------------------------------+--------------------------------
 *  1  | Recent updates                  | VAS_298_RecentUpdates
 *  2  | clients with activity           | VAS_298_ClientsWithActivity
 *  3  | All                             | VAS_298_All
 *  4  | Note logged                     | VAS_298_KindNote
 *  5  | Email logged                    | VAS_298_KindEmail
 *  6  | Call logged                     | VAS_298_KindCall
 *  7  | Meeting logged                  | VAS_298_KindMeeting
 *  8  | today                           | VAS_298_Today
 *  9  | yesterday                       | VAS_298_Yesterday
 * 10  | ago                             | VAS_298_Ago
 * 11  | Nothing here right now.         | VAS_298_NothingHere
 * 12  | Unable to load                  | VAS_298_UnableToLoad
 * 13  | of                              | VAS_298_Of
 * 14  | Showing                         | VAS_298_Showing
 * 15  | Previous page                   | VAS_298_PrevPage
 * 16  | Next page                       | VAS_298_NextPage
 * 17  | Customers with recent activity  | VAS_298_AllTitle
 * 18  | Close                           | VAS_298_Close
 * 19  | Customer details                | VAS_298_CustomerDetails
 * 20  | Tier                            | VAS_298_Tier
 * 21  | Segment                         | VAS_298_Segment
 * 22  | Owner                           | VAS_298_Owner
 * 23  | ARR                             | VAS_298_ARR
 * 24  | Open tickets                    | VAS_298_OpenTicketsFact
 * 25  | Projects                        | VAS_298_Projects
 * 26  | Pipeline                        | VAS_298_Pipeline
 * 27  | Onboarding                      | VAS_298_Onboarding
 * 28  | Key client                      | VAS_298_KeyClient
 * 29  | Signals                         | VAS_298_Signals
 * 30  | overdue                         | VAS_298_Overdue
 * 31  | days past due                   | VAS_298_DaysPastDue
 * 32  | open support tickets            | VAS_298_OpenSupportTickets
 * 33  | Key client — prioritise         | VAS_298_KeyPrioritise
 * 34  | Open support requests           | VAS_298_OpenRequests
 * 35  | Open record                     | VAS_298_OpenRecord
 * 36  | Active projects                 | VAS_298_ActiveProjects
 * 37  | Delayed                         | VAS_298_StateDelayed
 * 38  | On time                         | VAS_298_StateOnTime
 * 39  | Ongoing                         | VAS_298_StateOngoing
 * 40  | Upcoming                        | VAS_298_StateUpcoming
 * 41  | Completed                       | VAS_298_StateCompleted
 * 42  | Due                             | VAS_298_Due
 * 43  | activities                      | VAS_298_Activities
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

    // Kind-specific icon + colour, matching the Onfinity activity palette.
    var KIND_META = {
        Note: { icon: 'doc', color: '#5F4AA6', key: 'VAS_298_KindNote', fallback: 'Note logged' },
        Email: { icon: 'mail', color: '#0083DA', key: 'VAS_298_KindEmail', fallback: 'Email logged' },
        Call: { icon: 'phone', color: '#20A464', key: 'VAS_298_KindCall', fallback: 'Call logged' },
        Meeting: { icon: 'meet', color: '#D78B10', key: 'VAS_298_KindMeeting', fallback: 'Meeting logged' }
    };

    VAS.VAS_298_RecentUpdatesWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas298-root">');
        var $sub;
        var $body;

        var pageSize = 7;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;

        // All-list modal state.
        var $all, $allBody, $allPager, $allCount;
        var allOffset = 0, allTotal = 0, allSeq = 0;
        var ALL_PAGE = 25;

        // Detail modal state (reuses VAS_126 generic endpoint).
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
        // "today" / "yesterday" / "Nd ago" for the recency meta text.
        function daysAgo(days) {
            var n = Number(days || 0);
            if (n === 0) { return label('VAS_298_Today', 'today'); }
            if (n === 1) { return label('VAS_298_Yesterday', 'yesterday'); }
            return formatCount(n) + 'd ' + label('VAS_298_Ago', 'ago');
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
        // customer-detail endpoint reported.
        function formatMoney(value, cur) {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '';
            var iso = (cur && cur.iso) || '';
            var symbol = (cur && (cur.symbol || cur.iso)) || '';
            return sign + symbol + VIS.Util.formatCompactAmount(n, iso, precisionOf(cur));
        }
        // Projects fact: name the customer's delivery project rather than only
        // counting it (matches VAS_138/295/296/297).
        function projectText(name, count, dash) {
            if (!name) { return dash; }
            var extra = Number(count || 0) - 1;
            return escapeHtml(extra > 0 ? name + ' +' + formatCount(extra) : name);
        }

        function icon(name) {
            if (name === 'bell') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"></path><path d="M13.73 21a2 2 0 0 1-3.46 0"></path></svg>';
            }
            if (name === 'doc') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><path d="M14 2v6h6"></path></svg>';
            }
            if (name === 'mail') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="4" width="20" height="16" rx="2"></rect><path d="m22 7-10 5L2 7"></path></svg>';
            }
            if (name === 'phone') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"></path></svg>';
            }
            if (name === 'meet') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M15 10l4.55-2.27A1 1 0 0 1 21 8.62v6.76a1 1 0 0 1-1.45.89L15 14"></path><rect x="1" y="6" width="14" height="12" rx="2"></rect></svg>';
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
            var cls = 'vas298-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas298-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas298-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas298-tag-info'; }
            return '<span class="vas298-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }
        function stateLabel(state) {
            if (state === 'delayed') { return label('VAS_298_StateDelayed', 'Delayed'); }
            if (state === 'ontime') { return label('VAS_298_StateOnTime', 'On time'); }
            if (state === 'ongoing') { return label('VAS_298_StateOngoing', 'Ongoing'); }
            if (state === 'upcoming') { return label('VAS_298_StateUpcoming', 'Upcoming'); }
            return label('VAS_298_StateCompleted', 'Completed');
        }
        function projStatusClass(status) {
            if (status === 'Delayed') { return 'vas298-tag-delayed'; }
            if (status === 'Ongoing') { return 'vas298-tag-ongoing'; }
            if (status === 'Upcoming') { return 'vas298-tag-upcoming'; }
            return 'vas298-tag-ontime';
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_298_RecentUpdatesWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    listTotal = Number(data.total || 0);
                    $sub.text(formatCount(listTotal) + ' ' + label('VAS_298_ClientsWithActivity', 'clients with activity'));
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) { $body.html('<div class="vas298-state">' + escapeHtml(message) + '</div>'); }
        function renderErrorState() { $body.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_UnableToLoad', 'Unable to load')) + '</div>'); }

        function rowHtml(item) {
            var kindInfo = KIND_META[item.updateType] || KIND_META.Note;
            var kindText = escapeHtml(item.updateText) || escapeHtml(label(kindInfo.key, kindInfo.fallback));
            var meta = kindText + ' · ' + daysAgo(item.days);
            return '<div class="vas298-row" data-id="' + Number(item.customerId) + '">' +
                '<span class="vas298-row-ic" style="color:' + kindInfo.color + ';background:' + kindInfo.color + '1f">' + icon(kindInfo.icon) + '</span>' +
                '<span class="vas298-row-main">' +
                    '<span class="vas298-row-title" title="' + escapeHtml(item.customerName) + '">' + escapeHtml(item.customerName) + '</span>' +
                    '<span class="vas298-row-meta" title="' + meta + '">' + meta + '</span>' +
                '</span>' +
                tierTagHtml(item.tier) +
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
            var of = label('VAS_298_Of', 'of');
            var helper = label('VAS_298_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(total);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;
            return '<div class="vas298-pager">' +
                '<span class="vas298-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas298-pgctl">' +
                    '<button type="button" class="vas298-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_298_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas298-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas298-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_298_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) { renderState(label('VAS_298_NothingHere', 'Nothing here right now.')); return; }
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas298-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + items.map(rowHtml).join('') + '</div>' + pagerHtml(pageOffset, pageSize, listTotal));
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
            $('body').addClass('vas298-modal-open');
            loadAll();
        }
        function closeAll() {
            if (!$all) { return; }
            $all.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas298-modal-open'); }
        }
        function loadAll() {
            var seq = ++allSeq;
            $allBody.html('<div class="vas298-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $allPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_298_RecentUpdatesWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: allOffset, limit: ALL_PAGE },
                success: function (response) {
                    if (seq !== allSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $allBody.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    allTotal = Number(data.total || 0);
                    if ($allCount) { $allCount.text(formatCount(allTotal)); }
                    var items = data.items || [];
                    if (!items.length) { $allBody.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_NothingHere', 'Nothing here right now.')) + '</div>'); $allPager.empty(); return; }
                    $allBody.html('<div class="vas298-list">' + items.map(rowHtml).join('') + '</div>');
                    $allPager.html(pagerHtml(allOffset, ALL_PAGE, allTotal));
                },
                error: function () { if (seq === allSeq) { $allBody.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_UnableToLoad', 'Unable to load')) + '</div>'); } }
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
                '<div class="vas298-dialog" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_298_AllTitle', 'Customers with recent activity')) + '">' +
                    '<div class="vas298-scrim" data-all-close></div>' +
                    '<section class="vas298-panel">' +
                        '<header class="vas298-phead">' +
                            '<h2 class="vas298-ptitle">' + escapeHtml(label('VAS_298_AllTitle', 'Customers with recent activity')) + '</h2>' +
                            '<div class="vas298-phead-right"><span class="vas298-pcount"></span>' +
                                '<button type="button" class="vas298-close" data-all-close aria-label="' + escapeHtml(label('VAS_298_Close', 'Close')) + '">' + icon('close') + '</button></div>' +
                        '</header>' +
                        '<div class="vas298-pbody"></div>' +
                        '<footer class="vas298-pfoot"><div class="vas298-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($all);
            $allBody = $all.find('.vas298-pbody');
            $allPager = $all.find('.vas298-pager');
            $allCount = $all.find('.vas298-pcount');

            $all.on('click', '[data-all-close]', closeAll);
            $all.on('click', '.vas298-row', function (e) { e.stopPropagation(); openCustomer(Number($(this).attr('data-id'))); });
            $all.on('click', '.vas298-pgbtn', function () { turnAllPage($(this).attr('data-dir')); });
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

        function openCustomer(bpId) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas298-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas298-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas298-fact"><div class="vas298-fl">' + escapeHtml(fallback) + '</div><div class="vas298-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas298-signal"><span class="vas298-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas298-sig-main"><div class="vas298-sig-name">' + titleHtml + '</div><div class="vas298-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
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
            if (data.isKeyClient) { summaryParts.push(label('VAS_298_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas298-factgrid">' +
                fact(label('VAS_298_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_298_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_298_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_298_ARR', 'ARR'), escapeHtml(formatMoney(data.value, detailCurrency))) +
                fact(label('VAS_298_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_298_Projects', 'Projects'), projectText(data.projectName, projects, dash)) +
                fact(label('VAS_298_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatMoney(pipeline, detailCurrency)) : dash) +
                fact(label('VAS_298_Onboarding', 'Onboarding'), data.onboardingPercent == null ? dash : escapeHtml(formatCount(data.onboardingPercent) + '%')) +
            '</div>';

            var signals = '';
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatMoney(overdue, detailCurrency) + ' ' + label('VAS_298_Overdue', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_298_DaysPastDue', 'days past due'));
            }
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_298_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_298_KeyPrioritise', 'Key client — prioritise') : label('VAS_298_OpenRequests', 'Open support requests'));
            }
            var signalsBlock = signals ? '<div class="vas298-signals"><div class="vas298-sig-title">' + escapeHtml(label('VAS_298_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas298-id">' +
                '<span class="vas298-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas298-id-main"><div class="vas298-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas298-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas298-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas298-modal-open'); }
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
                '<div class="vas298-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_298_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas298-scrim" data-detail-close></div>' +
                    '<section class="vas298-dpanel">' +
                        '<header class="vas298-phead"><h2 class="vas298-ptitle">' + escapeHtml(label('VAS_298_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas298-phead-right"><span class="vas298-dsummary"></span>' +
                                '<button type="button" class="vas298-close" data-detail-close aria-label="' + escapeHtml(label('VAS_298_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas298-dbody"></div>' +
                        '<footer class="vas298-dfoot">' +
                            '<button type="button" class="vas298-btn vas298-btn-ghost" data-detail-act="projects">' + icon('folder') + escapeHtml(label('VAS_298_Projects', 'Projects')) + '</button>' +
                            '<button type="button" class="vas298-btn vas298-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_298_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas298-dbody');
            $detailSummary = $detail.find('.vas298-dsummary');
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
            $('body').addClass('vas298-modal-open');
            $projBody.html('<div class="vas298-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetProjects',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $projBody.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderProjects(data || {});
                },
                error: function () { $projBody.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }
        function closeProjects() {
            if (!$proj) { return; }
            if (document.activeElement && $proj[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $proj.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas298-modal-open'); }
        }
        function projStatCellHtml(state, count) {
            return '<button type="button" class="vas298-astat MPC-astat-' + state + (activeProjState === state ? ' is-active' : '') + '" data-state="' + state + '">' +
                '<b>' + formatCount(count) + '</b><span>' + escapeHtml(stateLabel(state)) + '</span></button>';
        }
        function projectRowHtml(project) {
            var total = Number(project.total || 0);
            var progress = Number(project.progressPercent || 0);
            var breakdown = PROJECT_STATES.map(function (s) {
                var n = Number(project[s] || 0);
                return n > 0 ? '<span class="vas298-bd MPC-bd-' + s + '">' + formatCount(n) + ' ' + escapeHtml(stateLabel(s)) + '</span>' : '';
            }).filter(function (x) { return x; }).join('');
            return '<div class="vas298-proj">' +
                '<div class="vas298-proj-head">' +
                    '<span class="vas298-proj-name" title="' + escapeHtml(project.name) + '">' + escapeHtml(project.name) + '</span>' +
                    '<span class="vas298-tag ' + projStatusClass(project.status) + '">' + escapeHtml(project.status) + '</span>' +
                '</div>' +
                (breakdown ? '<div class="vas298-proj-bd">' + breakdown + '</div>' : '') +
                '<div class="vas298-bar"><div style="width:' + Math.max(0, Math.min(100, progress)) + '%"></div></div>' +
                '<div class="vas298-proj-foot">' +
                    '<span>' + formatCount(project.completed) + '/' + formatCount(total) + ' ' + escapeHtml(label('VAS_298_Activities', 'activities')) + ' · ' + progress + '%</span>' +
                    '<span>' + escapeHtml(label('VAS_298_Due', 'Due')) + ' ' + escapeHtml(formatDate(project.dueDate)) + '</span>' +
                '</div>' +
            '</div>';
        }
        function renderProjects(data) {
            var summary = data.summary || {};
            var projects = data.projects || [];
            var cells = PROJECT_STATES.map(function (s) { return projStatCellHtml(s, summary[s] || 0); }).join('');
            var projRows = projects.length
                ? projects.map(projectRowHtml).join('')
                : '<div class="vas298-state">' + escapeHtml(label('VAS_298_NothingHere', 'Nothing here right now.')) + '</div>';
            $projBody.html(
                '<div class="vas298-statgrid">' + cells + '</div>' +
                '<div class="vas298-actdetail"></div>' +
                '<div class="vas298-projects">' + projRows + '</div>'
            );
        }
        function toggleProjState(state) {
            var $detailBox = $proj.find('.vas298-actdetail');
            if (activeProjState === state) {
                activeProjState = null;
                $proj.find('.vas298-astat').removeClass('is-active');
                $detailBox.empty();
                return;
            }
            activeProjState = state;
            $proj.find('.vas298-astat').removeClass('is-active').filter('[data-state="' + state + '"]').addClass('is-active');
            $detailBox.html('<div class="vas298-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetActivityDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: currentProjId, state: state },
                success: function (response) {
                    if (activeProjState !== state) { return; }
                    var data = parseResponse(response);
                    var items = (data && data.items) || [];
                    if (!items.length) { $detailBox.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_NothingHere', 'Nothing here right now.')) + '</div>'); return; }
                    var rows = items.map(function (a) {
                        return '<div class="vas298-act">' +
                            '<div class="vas298-act-main"><span class="vas298-act-name" title="' + escapeHtml(a.phaseName) + '">' + escapeHtml(a.phaseName) + '</span>' +
                                '<span class="vas298-act-proj" title="' + escapeHtml(a.projectName) + '">' + escapeHtml(a.projectName) + '</span></div>' +
                            '<span class="vas298-act-due">' + escapeHtml(label('VAS_298_Due', 'Due')) + ' ' + escapeHtml(formatDate(a.dueDate)) + '</span>' +
                        '</div>';
                    }).join('');
                    $detailBox.html('<div class="vas298-actlist">' + rows + '</div>');
                },
                error: function () { if (activeProjState === state) { $detailBox.html('<div class="vas298-state">' + escapeHtml(label('VAS_298_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }
        function createProjectsDialog() {
            $proj = $(
                '<div class="vas298-proj-modal" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_298_ActiveProjects', 'Active projects')) + '">' +
                    '<div class="vas298-scrim" data-proj-close></div>' +
                    '<section class="vas298-ppanel">' +
                        '<header class="vas298-phead"><h2 class="vas298-ptitle">' + escapeHtml(label('VAS_298_ActiveProjects', 'Active projects')) + '</h2>' +
                            '<div class="vas298-phead-right"><span class="vas298-psummary"></span>' +
                                '<button type="button" class="vas298-close" data-proj-close aria-label="' + escapeHtml(label('VAS_298_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas298-projbody"></div>' +
                        '<footer class="vas298-dfoot">' +
                            '<button type="button" class="vas298-btn vas298-btn-ghost" data-proj-close>' + escapeHtml(label('VAS_298_CustomerDetails', 'Customer details')) + '</button>' +
                            '<button type="button" class="vas298-btn vas298-btn-primary" data-proj-act="open">' + icon('arrow') + escapeHtml(label('VAS_298_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($proj);
            $projBody = $proj.find('.vas298-projbody');
            $projCust = $proj.find('.vas298-psummary');
            $proj.on('click', '[data-proj-close]', closeProjects);
            $proj.on('click', '[data-proj-act="open"]', function () { var id = currentProjId; closeProjects(); zoomToCustomer(id); });
            $proj.on('click', '.vas298-astat', function () { toggleProjState($(this).attr('data-state')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas298-card">' +
                    '<div class="vas298-head">' +
                        '<div class="vas298-head-l">' +
                            '<span class="vas298-iconwell">' + icon('bell') + '</span>' +
                            '<div class="vas298-head-txt"><div class="vas298-title">' + escapeHtml(label('VAS_298_RecentUpdates', 'Recent updates')) + '</div>' +
                                '<div class="vas298-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas298-alllink">' + escapeHtml(label('VAS_298_All', 'All')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas298-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas298-sub');
            $body = $card.find('.vas298-body');

            $card.on('click', '.vas298-alllink', function () { openAll(); });
            $card.on('click', '.vas298-row', function () { openCustomer(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas298-pgbtn', function () { turnPage($(this).attr('data-dir')); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createAllDialog();
            createDetailDialog();
            createProjectsDialog();
            $(document).on('keydown.MPCvas298', function (event) {
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
            $(document).off('keydown.MPCvas298');
            if ($all) { $all.remove(); $all = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($proj) { $proj.remove(); $proj = null; }
            $('body').removeClass('vas298-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_298_RecentUpdatesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_298_RecentUpdatesWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_298_RecentUpdatesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_298_RecentUpdatesWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_298_RecentUpdatesWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_298_RecentUpdatesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
