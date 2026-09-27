/**
 * VAS_295 Unresponded Emails Widget (Customers module dashboard)
 * Purpose - 3x1 compact glass list: customers whose newest e-mail is inbound
 *           and still unanswered, ranked by how long it has been waiting
 *           (oldest first). Each row shows the customer, the mail's subject
 *           (or "Email" where blank) and the owner, with a days-waiting tag
 *           that turns danger red at 7+ days (warning amber below that).
 *           Header shows "{n} awaiting reply" and an "All ->" link that opens
 *           the full server-paged list. Row / Open opens the customer detail
 *           modal (shared with VAS_126/138), which also shows the
 *           unresponded signal and, where the customer has active projects, a
 *           "Projects" action into the shared VAS_135 projects modal.
 * Design  - unresponded-emails.html (attached) + Design Specs/dashboard-widgets.md
 *           "Grid Data Widget". Glass surface, icon-well header, paged rows (3
 *           - compact 3x1 cell), circular pager. Internal sizing in em against
 *           the widget-root clamp; borders/radii in px. CSS namespaced vas295-*
 *           (MPC prefix rule).
 *
 * Backend - VAS_295_UnrespondedEmailsWidget/GetRows                (paged customer rows + total)
 *           Customer detail reuses the generic VAS_126 endpoint:
 *             VAS_126_OpenTicketsWidget/GetCustomerDetail
 *           Projects modal reuses VAS_135's endpoints (same pattern VAS_138
 *           already uses):
 *             VAS_135_ActiveProjectsWidget/GetProjects
 *             VAS_135_ActiveProjectsWidget/GetActivityDetail
 *
 * Routing - hosted on a window, opening the customer navigates the host grid
 *           in place (widgetFirevalueChanged); on the Home / landing dashboard
 *           (windowNo < 0) there is no host grid, so the record is opened in
 *           the standard Customer window via VAS.ZoomUtil. Matches VAS_135/138.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+----------------------------------+--------------------------------
 *  1  | Unresponded emails              | VAS_295_UnrespondedEmails
 *  2  | awaiting reply                  | VAS_295_AwaitingReply
 *  3  | All                             | VAS_295_All
 *  4  | Open                            | VAS_295_Open
 *  5  | Email                           | VAS_295_EmailFallback
 *  6  | No owner                        | VAS_295_NoOwner
 *  7  | Nothing here right now.         | VAS_295_NothingHere
 *  8  | Unable to load                  | VAS_295_UnableToLoad
 *  9  | of                              | VAS_295_Of
 * 10  | Showing                         | VAS_295_Showing
 * 11  | Previous page                   | VAS_295_PrevPage
 * 12  | Next page                       | VAS_295_NextPage
 * 13  | Customers awaiting a reply      | VAS_295_AllTitle
 * 14  | Close                           | VAS_295_Close
 * 15  | Customer details                | VAS_295_CustomerDetails
 * 16  | Tier                            | VAS_295_Tier
 * 17  | Segment                         | VAS_295_Segment
 * 18  | Owner                           | VAS_295_Owner
 * 19  | ARR                             | VAS_295_ARR
 * 20  | Open tickets                    | VAS_295_OpenTicketsFact
 * 21  | Projects                        | VAS_295_Projects
 * 22  | Pipeline                        | VAS_295_Pipeline
 * 23  | Onboarding                      | VAS_295_Onboarding
 * 24  | Key client                      | VAS_295_KeyClient
 * 25  | Signals                         | VAS_295_Signals
 * 26  | unanswered                      | VAS_295_Unanswered
 * 27  | Needs a reply                   | VAS_295_NeedsReply
 * 28  | overdue                         | VAS_295_Overdue
 * 29  | days past due                   | VAS_295_DaysPastDue
 * 30  | open support tickets            | VAS_295_OpenSupportTickets
 * 31  | Key client — prioritise         | VAS_295_KeyPrioritise
 * 32  | Open support requests           | VAS_295_OpenRequests
 * 33  | Open record                     | VAS_295_OpenRecord
 * 34  | Active projects                 | VAS_295_ActiveProjects
 * 35  | Delayed                         | VAS_295_StateDelayed
 * 36  | On time                         | VAS_295_StateOnTime
 * 37  | Ongoing                         | VAS_295_StateOngoing
 * 38  | Upcoming                        | VAS_295_StateUpcoming
 * 39  | Completed                       | VAS_295_StateCompleted
 * 40  | Due                             | VAS_295_Due
 * 41  | activities                      | VAS_295_Activities
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

    VAS.VAS_295_UnrespondedEmailsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas295-root">');
        var $sub, $body;

        // Compact 3x1 cell: 3 rows per page.
        var pageSize = 3;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;

        // All-list modal state.
        var $all, $allBody, $allPager, $allCount;
        var allOffset = 0, allTotal = 0, allSeq = 0;
        var ALL_PAGE = 25;

        // Detail modal state (reuses VAS_126 generic endpoint). The row's own
        // mail subject/age is carried alongside so the modal can show the
        // unresponded signal without a second round trip - GetCustomerDetail
        // does not know about this widget's data.
        var $detail, $detailBody, $detailSummary, currentDetailId = 0, currentDetailName = '';
        var currentDetailMail = { title: '', days: 0 };
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

        // Standard precision of the supplied base-currency descriptor, falling
        // back to the session context when the endpoint did not send one.
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
        // counting it (matches VAS_138).
        function projectText(name, count, dash) {
            if (!name) { return dash; }
            var extra = Number(count || 0) - 1;
            return escapeHtml(extra > 0 ? name + ' +' + formatCount(extra) : name);
        }

        function icon(name) {
            if (name === 'mail') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="4" width="20" height="16" rx="2"></rect><path d="m22 7-10 5L2 7"></path></svg>';
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
            var cls = 'vas295-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas295-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas295-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas295-tag-info'; }
            return '<span class="vas295-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }
        function stateLabel(state) {
            if (state === 'delayed') { return label('VAS_295_StateDelayed', 'Delayed'); }
            if (state === 'ontime') { return label('VAS_295_StateOnTime', 'On time'); }
            if (state === 'ongoing') { return label('VAS_295_StateOngoing', 'Ongoing'); }
            if (state === 'upcoming') { return label('VAS_295_StateUpcoming', 'Upcoming'); }
            return label('VAS_295_StateCompleted', 'Completed');
        }
        function projStatusClass(status) {
            if (status === 'Delayed') { return 'vas295-tag-delayed'; }
            if (status === 'Ongoing') { return 'vas295-tag-ongoing'; }
            if (status === 'Upcoming') { return 'vas295-tag-upcoming'; }
            return 'vas295-tag-ontime';
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_295_UnrespondedEmailsWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    listTotal = Number(data.total || 0);
                    $sub.text(formatCount(listTotal) + ' ' + label('VAS_295_AwaitingReply', 'awaiting reply'));
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) { $body.html('<div class="vas295-state">' + escapeHtml(message) + '</div>'); }
        function renderErrorState() { $body.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_UnableToLoad', 'Unable to load')) + '</div>'); }

        // Days-waiting tag turns danger red at 7+ days, warning amber below that.
        function daysTagHtml(days) {
            var cls = days >= 7 ? 'vas295-tag-delayed' : 'vas295-tag-warn';
            return '<span class="vas295-tag ' + cls + '">' + formatCount(days) + 'd</span>';
        }

        function rowHtml(item) {
            var owner = item.ownerName || label('VAS_295_NoOwner', 'No owner');
            var subject = item.mailTitle || label('VAS_295_EmailFallback', 'Email');
            var meta = subject + ' · ' + owner;
            var days = Number(item.unrespDays || 0);
            return '<div class="vas295-row" data-id="' + Number(item.customerId) + '" data-title="' + escapeHtml(subject) + '" data-days="' + days + '">' +
                '<span class="vas295-row-ic">' + icon('mail') + '</span>' +
                '<span class="vas295-row-main">' +
                    '<span class="vas295-row-title" title="' + escapeHtml(item.customerName) + '">' + escapeHtml(item.customerName) + '</span>' +
                    '<span class="vas295-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                daysTagHtml(days) +
                '<button type="button" class="vas295-open" data-id="' + Number(item.customerId) + '" data-title="' + escapeHtml(subject) + '" data-days="' + days + '">' + escapeHtml(label('VAS_295_Open', 'Open')) + '</button>' +
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
            var of = label('VAS_295_Of', 'of');
            var helper = label('VAS_295_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(total);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;
            return '<div class="vas295-pager">' +
                '<span class="vas295-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas295-pgctl">' +
                    '<button type="button" class="vas295-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_295_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas295-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas295-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_295_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) { renderState(label('VAS_295_NothingHere', 'Nothing here right now.')); return; }
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas295-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + items.map(rowHtml).join('') + '</div>' + pagerHtml(pageOffset, pageSize, listTotal));
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
            $('body').addClass('vas295-modal-open');
            loadAll();
        }
        function closeAll() {
            if (!$all) { return; }
            $all.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas295-modal-open'); }
        }
        function loadAll() {
            var seq = ++allSeq;
            $allBody.html('<div class="vas295-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $allPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_295_UnrespondedEmailsWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: allOffset, limit: ALL_PAGE },
                success: function (response) {
                    if (seq !== allSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $allBody.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    allTotal = Number(data.total || 0);
                    if ($allCount) { $allCount.text(formatCount(allTotal)); }
                    var items = data.items || [];
                    if (!items.length) { $allBody.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_NothingHere', 'Nothing here right now.')) + '</div>'); return; }
                    $allBody.html('<div class="vas295-list">' + items.map(rowHtml).join('') + '</div>');
                    $allPager.html(pagerHtml(allOffset, ALL_PAGE, allTotal));
                },
                error: function () { if (seq === allSeq) { $allBody.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_UnableToLoad', 'Unable to load')) + '</div>'); } }
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
                '<div class="vas295-dialog" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_295_AllTitle', 'Customers awaiting a reply')) + '">' +
                    '<div class="vas295-scrim" data-all-close></div>' +
                    '<section class="vas295-panel">' +
                        '<header class="vas295-phead"><h2 class="vas295-ptitle">' + escapeHtml(label('VAS_295_AllTitle', 'Customers awaiting a reply')) + '</h2>' +
                            '<div class="vas295-phead-right"><span class="vas295-pcount"></span>' +
                                '<button type="button" class="vas295-close" data-all-close aria-label="' + escapeHtml(label('VAS_295_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas295-pbody"></div>' +
                        '<footer class="vas295-pfoot"><div class="vas295-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($all);
            $allBody = $all.find('.vas295-pbody');
            $allPager = $all.find('.vas295-pager');
            $allCount = $all.find('.vas295-pcount');
            $all.on('click', '[data-all-close]', closeAll);
            $all.on('click', '.vas295-row, .vas295-open', function (e) {
                e.stopPropagation();
                openCustomer(Number($(this).attr('data-id')), $(this).attr('data-title'), Number($(this).attr('data-days')));
            });
            $all.on('click', '.vas295-pgbtn', function () { turnAllPage($(this).attr('data-dir')); });
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

        function openCustomer(bpId, mailTitle, mailDays) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            currentDetailMail = { title: mailTitle || '', days: Number(mailDays || 0) };
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas295-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas295-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas295-fact"><div class="vas295-fl">' + escapeHtml(fallback) + '</div><div class="vas295-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas295-signal"><span class="vas295-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas295-sig-main"><div class="vas295-sig-name">' + titleHtml + '</div><div class="vas295-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
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
            if (data.isKeyClient) { summaryParts.push(label('VAS_295_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas295-factgrid">' +
                fact(label('VAS_295_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_295_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_295_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_295_ARR', 'ARR'), escapeHtml(formatMoney(data.value, detailCurrency))) +
                fact(label('VAS_295_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_295_Projects', 'Projects'), projectText(data.projectName, projects, dash)) +
                fact(label('VAS_295_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatMoney(pipeline, detailCurrency)) : dash) +
                fact(label('VAS_295_Onboarding', 'Onboarding'), data.onboardingPercent == null ? dash : escapeHtml(formatCount(data.onboardingPercent) + '%')) +
            '</div>';

            // This widget's own signal - the row's own known subject/age, since
            // the shared GetCustomerDetail endpoint carries no mail data. Every
            // row that can open this modal is, by construction, awaiting a
            // reply, so the signal always applies.
            var signals = signalRow('mail', '#D78B10', escapeHtml((currentDetailMail.title || label('VAS_295_EmailFallback', 'Email')) + ' ' + label('VAS_295_Unanswered', 'unanswered') + ' ' + formatCount(currentDetailMail.days) + 'd'),
                label('VAS_295_NeedsReply', 'Needs a reply'));
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatMoney(overdue, detailCurrency) + ' ' + label('VAS_295_Overdue', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_295_DaysPastDue', 'days past due'));
            }
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_295_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_295_KeyPrioritise', 'Key client — prioritise') : label('VAS_295_OpenRequests', 'Open support requests'));
            }
            var signalsBlock = signals ? '<div class="vas295-signals"><div class="vas295-sig-title">' + escapeHtml(label('VAS_295_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas295-id">' +
                '<span class="vas295-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas295-id-main"><div class="vas295-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas295-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas295-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas295-modal-open'); }
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
                '<div class="vas295-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_295_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas295-scrim" data-detail-close></div>' +
                    '<section class="vas295-dpanel">' +
                        '<header class="vas295-phead"><h2 class="vas295-ptitle">' + escapeHtml(label('VAS_295_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas295-phead-right"><span class="vas295-dsummary"></span>' +
                                '<button type="button" class="vas295-close" data-detail-close aria-label="' + escapeHtml(label('VAS_295_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas295-dbody"></div>' +
                        '<footer class="vas295-dfoot">' +
                            '<button type="button" class="vas295-btn vas295-btn-ghost" data-detail-act="projects">' + icon('folder') + escapeHtml(label('VAS_295_Projects', 'Projects')) + '</button>' +
                            '<button type="button" class="vas295-btn vas295-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_295_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas295-dbody');
            $detailSummary = $detail.find('.vas295-dsummary');
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
            $('body').addClass('vas295-modal-open');
            $projBody.html('<div class="vas295-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetProjects',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $projBody.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderProjects(data || {});
                },
                error: function () { $projBody.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }
        function closeProjects() {
            if (!$proj) { return; }
            if (document.activeElement && $proj[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $proj.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas295-modal-open'); }
        }
        function projStatCellHtml(state, count) {
            return '<button type="button" class="vas295-astat MPC-astat-' + state + (activeProjState === state ? ' is-active' : '') + '" data-state="' + state + '">' +
                '<b>' + formatCount(count) + '</b><span>' + escapeHtml(stateLabel(state)) + '</span></button>';
        }
        function projectRowHtml(project) {
            var total = Number(project.total || 0);
            var progress = Number(project.progressPercent || 0);
            var breakdown = PROJECT_STATES.map(function (s) {
                var n = Number(project[s] || 0);
                return n > 0 ? '<span class="vas295-bd MPC-bd-' + s + '">' + formatCount(n) + ' ' + escapeHtml(stateLabel(s)) + '</span>' : '';
            }).filter(function (x) { return x; }).join('');
            return '<div class="vas295-proj">' +
                '<div class="vas295-proj-head">' +
                    '<span class="vas295-proj-name" title="' + escapeHtml(project.name) + '">' + escapeHtml(project.name) + '</span>' +
                    '<span class="vas295-tag ' + projStatusClass(project.status) + '">' + escapeHtml(project.status) + '</span>' +
                '</div>' +
                (breakdown ? '<div class="vas295-proj-bd">' + breakdown + '</div>' : '') +
                '<div class="vas295-bar"><div style="width:' + Math.max(0, Math.min(100, progress)) + '%"></div></div>' +
                '<div class="vas295-proj-foot">' +
                    '<span>' + formatCount(project.completed) + '/' + formatCount(total) + ' ' + escapeHtml(label('VAS_295_Activities', 'activities')) + ' · ' + progress + '%</span>' +
                    '<span>' + escapeHtml(label('VAS_295_Due', 'Due')) + ' ' + escapeHtml(formatDate(project.dueDate)) + '</span>' +
                '</div>' +
            '</div>';
        }
        function renderProjects(data) {
            var summary = data.summary || {};
            var projects = data.projects || [];
            var cells = PROJECT_STATES.map(function (s) { return projStatCellHtml(s, summary[s] || 0); }).join('');
            var projRows = projects.length
                ? projects.map(projectRowHtml).join('')
                : '<div class="vas295-state">' + escapeHtml(label('VAS_295_NothingHere', 'Nothing here right now.')) + '</div>';
            $projBody.html(
                '<div class="vas295-statgrid">' + cells + '</div>' +
                '<div class="vas295-actdetail"></div>' +
                '<div class="vas295-projects">' + projRows + '</div>'
            );
        }
        function toggleProjState(state) {
            var $detailBox = $proj.find('.vas295-actdetail');
            if (activeProjState === state) {
                activeProjState = null;
                $proj.find('.vas295-astat').removeClass('is-active');
                $detailBox.empty();
                return;
            }
            activeProjState = state;
            $proj.find('.vas295-astat').removeClass('is-active').filter('[data-state="' + state + '"]').addClass('is-active');
            $detailBox.html('<div class="vas295-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetActivityDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: currentProjId, state: state },
                success: function (response) {
                    if (activeProjState !== state) { return; }
                    var data = parseResponse(response);
                    var items = (data && data.items) || [];
                    if (!items.length) { $detailBox.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_NothingHere', 'Nothing here right now.')) + '</div>'); return; }
                    var rows = items.map(function (a) {
                        return '<div class="vas295-act">' +
                            '<div class="vas295-act-main"><span class="vas295-act-name" title="' + escapeHtml(a.phaseName) + '">' + escapeHtml(a.phaseName) + '</span>' +
                                '<span class="vas295-act-proj" title="' + escapeHtml(a.projectName) + '">' + escapeHtml(a.projectName) + '</span></div>' +
                            '<span class="vas295-act-due">' + escapeHtml(label('VAS_295_Due', 'Due')) + ' ' + escapeHtml(formatDate(a.dueDate)) + '</span>' +
                        '</div>';
                    }).join('');
                    $detailBox.html('<div class="vas295-actlist">' + rows + '</div>');
                },
                error: function () { if (activeProjState === state) { $detailBox.html('<div class="vas295-state">' + escapeHtml(label('VAS_295_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }
        function createProjectsDialog() {
            $proj = $(
                '<div class="vas295-proj-modal" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_295_ActiveProjects', 'Active projects')) + '">' +
                    '<div class="vas295-scrim" data-proj-close></div>' +
                    '<section class="vas295-ppanel">' +
                        '<header class="vas295-phead"><h2 class="vas295-ptitle">' + escapeHtml(label('VAS_295_ActiveProjects', 'Active projects')) + '</h2>' +
                            '<div class="vas295-phead-right"><span class="vas295-psummary"></span>' +
                                '<button type="button" class="vas295-close" data-proj-close aria-label="' + escapeHtml(label('VAS_295_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas295-projbody"></div>' +
                        '<footer class="vas295-dfoot">' +
                            '<button type="button" class="vas295-btn vas295-btn-ghost" data-proj-close>' + escapeHtml(label('VAS_295_CustomerDetails', 'Customer details')) + '</button>' +
                            '<button type="button" class="vas295-btn vas295-btn-primary" data-proj-act="open">' + icon('arrow') + escapeHtml(label('VAS_295_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($proj);
            $projBody = $proj.find('.vas295-projbody');
            $projCust = $proj.find('.vas295-psummary');
            $proj.on('click', '[data-proj-close]', closeProjects);
            $proj.on('click', '[data-proj-act="open"]', function () { var id = currentProjId; closeProjects(); zoomToCustomer(id); });
            $proj.on('click', '.vas295-astat', function () { toggleProjState($(this).attr('data-state')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas295-card">' +
                    '<div class="vas295-head">' +
                        '<div class="vas295-head-l">' +
                            '<span class="vas295-iconwell">' + icon('mail') + '</span>' +
                            '<div class="vas295-head-txt"><div class="vas295-title">' + escapeHtml(label('VAS_295_UnrespondedEmails', 'Unresponded emails')) + '</div>' +
                                '<div class="vas295-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas295-alllink">' + escapeHtml(label('VAS_295_All', 'All')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas295-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas295-sub');
            $body = $card.find('.vas295-body');
            $card.on('click', '.vas295-alllink', function () { openAll(); });
            $card.on('click', '.vas295-open', function (e) {
                e.stopPropagation();
                openCustomer(Number($(this).attr('data-id')), $(this).attr('data-title'), Number($(this).attr('data-days')));
            });
            $card.on('click', '.vas295-row', function () {
                openCustomer(Number($(this).attr('data-id')), $(this).attr('data-title'), Number($(this).attr('data-days')));
            });
            $card.on('click', '.vas295-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createAllDialog();
            createDetailDialog();
            createProjectsDialog();
            $(document).on('keydown.MPCvas295', function (event) {
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
            $(document).off('keydown.MPCvas295');
            if ($all) { $all.remove(); $all = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($proj) { $proj.remove(); $proj = null; }
            $('body').removeClass('vas295-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_295_UnrespondedEmailsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_295_UnrespondedEmailsWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_295_UnrespondedEmailsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_295_UnrespondedEmailsWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_295_UnrespondedEmailsWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_295_UnrespondedEmailsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
