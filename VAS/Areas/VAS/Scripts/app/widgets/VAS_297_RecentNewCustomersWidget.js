/**
 * VAS_297 Recent New Customers Widget (Customers module dashboard)
 * Purpose - 3x3 glass list: customers created in the last 45 days, newest
 *           first, each row showing an onboarded tag (green) or the current
 *           onboarding step (amber) - ties new-customer arrival to onboarding
 *           progress. Header shows "{n} joined in 45 days" and pages 7 rows
 *           with the shared circular pager. Row click opens the customer
 *           detail modal (shared with VAS_126/138/295/296); an in-progress
 *           customer's onboarding also renders there as a progress bar,
 *           which the generic GetCustomerDetail payload does not carry on
 *           its own, so the row's own known step/percent travel alongside
 *           the click.
 * Design  - recent-new-customers.html (attached) + Design Specs/
 *           dashboard-widgets.md "Grid Data Widget". Glass surface,
 *           icon-well header, ranked rows (paged 7), circular pager.
 *           Internal sizing in em against the widget-root clamp; borders/
 *           radii in px. CSS namespaced vas297-* (MPC prefix rule).
 *
 *           NOTE 2026-09-23: the build brief and the reference file's own
 *           on-page note both say "3x1", but that file's actual preview
 *           height (360px) and its pagedList(...) call (page size 7, the
 *           shared default) are identical to the 3x2 open-proposals.html -
 *           not to the true compact 3x1 references (delayed-projects.html /
 *           unresponded-emails.html), which use a 260px preview and page 3.
 *           Confirmed with the requester: built as 3x3, paged 7, at the
 *           full (non-compact) widget scale - matches VAS_135/138/296.
 *
 * Backend - VAS_297_RecentNewCustomersWidget/GetRows                (paged customer rows + total)
 *           Customer detail reuses the generic VAS_126 endpoint:
 *             VAS_126_OpenTicketsWidget/GetCustomerDetail
 *           Projects modal reuses VAS_135's endpoints (same pattern VAS_138/
 *           295/296 already use):
 *             VAS_135_ActiveProjectsWidget/GetProjects
 *             VAS_135_ActiveProjectsWidget/GetActivityDetail
 *
 * Routing - hosted on a window, opening the customer navigates the host grid
 *           in place (widgetFirevalueChanged); on the Home / landing
 *           dashboard (windowNo < 0) there is no host grid, so the record is
 *           opened in the standard Customer window via VAS.ZoomUtil.
 *           Matches VAS_135/138/295/296.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+----------------------------------+--------------------------------
 *  1  | Recent new customers            | VAS_297_RecentNewCustomers
 *  2  | joined in 45 days               | VAS_297_JoinedIn45Days
 *  3  | Joined                          | VAS_297_Joined
 *  4  | No owner                        | VAS_297_NoOwner
 *  5  | Onboarded                       | VAS_297_Onboarded
 *  6  | today                           | VAS_297_Today
 *  7  | yesterday                       | VAS_297_Yesterday
 *  8  | ago                             | VAS_297_Ago
 *  9  | Nothing here right now.         | VAS_297_NothingHere
 * 10  | Unable to load                  | VAS_297_UnableToLoad
 * 11  | of                              | VAS_297_Of
 * 12  | Showing                         | VAS_297_Showing
 * 13  | Previous page                   | VAS_297_PrevPage
 * 14  | Next page                       | VAS_297_NextPage
 * 15  | Close                           | VAS_297_Close
 * 16  | Customer details                | VAS_297_CustomerDetails
 * 17  | Tier                            | VAS_297_Tier
 * 18  | Segment                         | VAS_297_Segment
 * 19  | Owner                           | VAS_297_Owner
 * 20  | ARR                             | VAS_297_ARR
 * 21  | Open tickets                    | VAS_297_OpenTicketsFact
 * 22  | Projects                        | VAS_297_Projects
 * 23  | Pipeline                        | VAS_297_Pipeline
 * 24  | Onboarding                      | VAS_297_Onboarding
 * 25  | Key client                      | VAS_297_KeyClient
 * 26  | Signals                         | VAS_297_Signals
 * 27  | overdue                         | VAS_297_Overdue
 * 28  | days past due                   | VAS_297_DaysPastDue
 * 29  | open support tickets            | VAS_297_OpenSupportTickets
 * 30  | Key client — prioritise         | VAS_297_KeyPrioritise
 * 31  | Open support requests           | VAS_297_OpenRequests
 * 32  | Open record                     | VAS_297_OpenRecord
 * 33  | Active projects                 | VAS_297_ActiveProjects
 * 34  | Delayed                         | VAS_297_StateDelayed
 * 35  | On time                         | VAS_297_StateOnTime
 * 36  | Ongoing                         | VAS_297_StateOngoing
 * 37  | Upcoming                        | VAS_297_StateUpcoming
 * 38  | Completed                       | VAS_297_StateCompleted
 * 39  | Due                             | VAS_297_Due
 * 40  | activities                      | VAS_297_Activities
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

    VAS.VAS_297_RecentNewCustomersWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas297-root">');
        var $sub;
        var $body;

        var pageSize = 7;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;

        // Detail modal state (reuses VAS_126 generic endpoint). The row's own
        // onboarding step is carried alongside so the modal can name the step
        // next to the fetched percentage without a second round trip.
        var $detail, $detailBody, $detailSummary, currentDetailId = 0, currentDetailName = '';
        var currentDetailOnbStep = '';
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
        // "today" / "yesterday" / "Nd ago" for the joined-date meta line.
        function daysAgo(days) {
            var n = Number(days || 0);
            if (n === 0) { return label('VAS_297_Today', 'today'); }
            if (n === 1) { return label('VAS_297_Yesterday', 'yesterday'); }
            return formatCount(n) + 'd ' + label('VAS_297_Ago', 'ago');
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
        // counting it (matches VAS_138/295/296).
        function projectText(name, count, dash) {
            if (!name) { return dash; }
            var extra = Number(count || 0) - 1;
            return escapeHtml(extra > 0 ? name + ' +' + formatCount(extra) : name);
        }

        function icon(name) {
            if (name === 'spark') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3l1.8 4.2L18 9l-4.2 1.8L12 15l-1.8-4.2L6 9l4.2-1.8z"></path></svg>';
            }
            if (name === 'rocket') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.71-.84.7-2.13-.09-2.91a2.18 2.18 0 0 0-2.91-.09z"></path><path d="m12 15-3-3a22 22 0 0 1 2-3.95A12.88 12.88 0 0 1 22 2c0 2.72-.78 7.5-6 11a22.35 22.35 0 0 1-4 2z"></path><path d="M9 12H4s.55-3.03 2-4c1.62-1.08 5 0 5 0"></path></svg>';
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
            var cls = 'vas297-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas297-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas297-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas297-tag-info'; }
            return '<span class="vas297-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }
        function stateLabel(state) {
            if (state === 'delayed') { return label('VAS_297_StateDelayed', 'Delayed'); }
            if (state === 'ontime') { return label('VAS_297_StateOnTime', 'On time'); }
            if (state === 'ongoing') { return label('VAS_297_StateOngoing', 'Ongoing'); }
            if (state === 'upcoming') { return label('VAS_297_StateUpcoming', 'Upcoming'); }
            return label('VAS_297_StateCompleted', 'Completed');
        }
        function projStatusClass(status) {
            if (status === 'Delayed') { return 'vas297-tag-delayed'; }
            if (status === 'Ongoing') { return 'vas297-tag-ongoing'; }
            if (status === 'Upcoming') { return 'vas297-tag-upcoming'; }
            return 'vas297-tag-ontime';
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_297_RecentNewCustomersWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    listTotal = Number(data.total || 0);
                    $sub.text(formatCount(listTotal) + ' ' + label('VAS_297_JoinedIn45Days', 'joined in 45 days'));
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) { $body.html('<div class="vas297-state">' + escapeHtml(message) + '</div>'); }
        function renderErrorState() { $body.html('<div class="vas297-state">' + escapeHtml(label('VAS_297_UnableToLoad', 'Unable to load')) + '</div>'); }

        function rowHtml(item) {
            var name = item.customerName || '';
            var owner = item.ownerName || label('VAS_297_NoOwner', 'No owner');
            var meta = label('VAS_297_Joined', 'Joined') + ' ' + daysAgo(item.newDays) + ' · ' + owner;
            var tag = item.onbDone
                ? '<span class="vas297-tag vas297-tag-ontime">' + escapeHtml(label('VAS_297_Onboarded', 'Onboarded')) + '</span>'
                : '<span class="vas297-tag vas297-tag-warn">' + escapeHtml(item.onbStep) + '</span>';
            return '<div class="vas297-row" data-id="' + Number(item.customerId) + '" data-step="' + escapeHtml(item.onbStep) + '">' +
                '<span class="vas297-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<span class="vas297-row-main">' +
                    '<span class="vas297-row-title" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                    '<span class="vas297-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                tag +
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
            var of = label('VAS_297_Of', 'of');
            var helper = label('VAS_297_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(total);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;
            return '<div class="vas297-pager">' +
                '<span class="vas297-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas297-pgctl">' +
                    '<button type="button" class="vas297-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_297_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas297-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas297-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_297_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) { renderState(label('VAS_297_NothingHere', 'Nothing here right now.')); return; }
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas297-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + items.map(rowHtml).join('') + '</div>' + pagerHtml(pageOffset, pageSize, listTotal));
        }

        function turnPage(direction) {
            var next = pageOffset + (direction === 'next' ? pageSize : -pageSize);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            pageOffset = next;
            loadRows();
        }

        // No "All" list modal for this widget - the reference newCustWidget()
        // calls whead() with no fourth ("right") argument, so there is no
        // "All ->" link. The 45-day window is already a bounded population the
        // in-widget pager covers in full.
        function anyModalOpen() {
            return ($detail && $detail.hasClass('is-open')) || ($proj && $proj.hasClass('is-open'));
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

        function openCustomer(bpId, onbStep) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            currentDetailOnbStep = onbStep || '';
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas297-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas297-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas297-state">' + escapeHtml(label('VAS_297_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas297-state">' + escapeHtml(label('VAS_297_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas297-fact"><div class="vas297-fl">' + escapeHtml(fallback) + '</div><div class="vas297-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailHtml) {
            return '<div class="vas297-signal"><span class="vas297-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas297-sig-main"><div class="vas297-sig-name">' + titleHtml + '</div><div class="vas297-sig-detail">' + detailHtml + '</div></div></div>';
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
            if (data.isKeyClient) { summaryParts.push(label('VAS_297_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas297-factgrid">' +
                fact(label('VAS_297_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_297_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_297_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_297_ARR', 'ARR'), escapeHtml(formatMoney(data.value, detailCurrency))) +
                fact(label('VAS_297_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_297_Projects', 'Projects'), projectText(data.projectName, projects, dash)) +
                fact(label('VAS_297_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatMoney(pipeline, detailCurrency)) : dash) +
                fact(label('VAS_297_Onboarding', 'Onboarding'), data.onboardingPercent == null ? dash : escapeHtml(formatCount(data.onboardingPercent) + '%')) +
            '</div>';

            var signals = '';
            // In-progress onboarding gets its own progress-bar signal (this
            // widget's brief calls for it specifically). Done customers show
            // nothing here - the fact grid above already reads "100%".
            var onbPct = data.onboardingPercent;
            if (onbPct != null && Number(onbPct) < 100) {
                var pct = Math.max(0, Math.min(100, Number(onbPct)));
                var stepText = currentDetailOnbStep ? ' · ' + escapeHtml(currentDetailOnbStep) : '';
                signals += '<div class="vas297-signal"><span class="vas297-sig-ic" style="color:#0083DA;background:#0083DA1f">' + icon('rocket') + '</span>' +
                    '<div class="vas297-sig-main"><div class="vas297-sig-name">' + escapeHtml(label('VAS_297_Onboarding', 'Onboarding')) + ' ' + formatCount(pct) + '%' + stepText + '</div>' +
                    '<div class="vas297-onbbar"><div style="width:' + pct + '%"></div></div></div></div>';
            }
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatMoney(overdue, detailCurrency) + ' ' + label('VAS_297_Overdue', 'overdue') + invPart),
                    escapeHtml(formatCount(data.overdueDays || 0) + ' ' + label('VAS_297_DaysPastDue', 'days past due')));
            }
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_297_OpenSupportTickets', 'open support tickets')),
                    escapeHtml(data.isKeyClient ? label('VAS_297_KeyPrioritise', 'Key client — prioritise') : label('VAS_297_OpenRequests', 'Open support requests')));
            }
            var signalsBlock = signals ? '<div class="vas297-signals"><div class="vas297-sig-title">' + escapeHtml(label('VAS_297_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas297-id">' +
                '<span class="vas297-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas297-id-main"><div class="vas297-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas297-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas297-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas297-modal-open'); }
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
                '<div class="vas297-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_297_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas297-scrim" data-detail-close></div>' +
                    '<section class="vas297-dpanel">' +
                        '<header class="vas297-phead"><h2 class="vas297-ptitle">' + escapeHtml(label('VAS_297_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas297-phead-right"><span class="vas297-dsummary"></span>' +
                                '<button type="button" class="vas297-close" data-detail-close aria-label="' + escapeHtml(label('VAS_297_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas297-dbody"></div>' +
                        '<footer class="vas297-dfoot">' +
                            '<button type="button" class="vas297-btn vas297-btn-ghost" data-detail-act="projects">' + icon('folder') + escapeHtml(label('VAS_297_Projects', 'Projects')) + '</button>' +
                            '<button type="button" class="vas297-btn vas297-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_297_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas297-dbody');
            $detailSummary = $detail.find('.vas297-dsummary');
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
            $('body').addClass('vas297-modal-open');
            $projBody.html('<div class="vas297-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetProjects',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $projBody.html('<div class="vas297-state">' + escapeHtml(label('VAS_297_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderProjects(data || {});
                },
                error: function () { $projBody.html('<div class="vas297-state">' + escapeHtml(label('VAS_297_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }
        function closeProjects() {
            if (!$proj) { return; }
            if (document.activeElement && $proj[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $proj.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas297-modal-open'); }
        }
        function projStatCellHtml(state, count) {
            return '<button type="button" class="vas297-astat MPC-astat-' + state + (activeProjState === state ? ' is-active' : '') + '" data-state="' + state + '">' +
                '<b>' + formatCount(count) + '</b><span>' + escapeHtml(stateLabel(state)) + '</span></button>';
        }
        function projectRowHtml(project) {
            var total = Number(project.total || 0);
            var progress = Number(project.progressPercent || 0);
            var breakdown = PROJECT_STATES.map(function (s) {
                var n = Number(project[s] || 0);
                return n > 0 ? '<span class="vas297-bd MPC-bd-' + s + '">' + formatCount(n) + ' ' + escapeHtml(stateLabel(s)) + '</span>' : '';
            }).filter(function (x) { return x; }).join('');
            return '<div class="vas297-proj">' +
                '<div class="vas297-proj-head">' +
                    '<span class="vas297-proj-name" title="' + escapeHtml(project.name) + '">' + escapeHtml(project.name) + '</span>' +
                    '<span class="vas297-tag ' + projStatusClass(project.status) + '">' + escapeHtml(project.status) + '</span>' +
                '</div>' +
                (breakdown ? '<div class="vas297-proj-bd">' + breakdown + '</div>' : '') +
                '<div class="vas297-bar"><div style="width:' + Math.max(0, Math.min(100, progress)) + '%"></div></div>' +
                '<div class="vas297-proj-foot">' +
                    '<span>' + formatCount(project.completed) + '/' + formatCount(total) + ' ' + escapeHtml(label('VAS_297_Activities', 'activities')) + ' · ' + progress + '%</span>' +
                    '<span>' + escapeHtml(label('VAS_297_Due', 'Due')) + ' ' + escapeHtml(formatDate(project.dueDate)) + '</span>' +
                '</div>' +
            '</div>';
        }
        function renderProjects(data) {
            var summary = data.summary || {};
            var projects = data.projects || [];
            var cells = PROJECT_STATES.map(function (s) { return projStatCellHtml(s, summary[s] || 0); }).join('');
            var projRows = projects.length
                ? projects.map(projectRowHtml).join('')
                : '<div class="vas297-state">' + escapeHtml(label('VAS_297_NothingHere', 'Nothing here right now.')) + '</div>';
            $projBody.html(
                '<div class="vas297-statgrid">' + cells + '</div>' +
                '<div class="vas297-actdetail"></div>' +
                '<div class="vas297-projects">' + projRows + '</div>'
            );
        }
        function toggleProjState(state) {
            var $detailBox = $proj.find('.vas297-actdetail');
            if (activeProjState === state) {
                activeProjState = null;
                $proj.find('.vas297-astat').removeClass('is-active');
                $detailBox.empty();
                return;
            }
            activeProjState = state;
            $proj.find('.vas297-astat').removeClass('is-active').filter('[data-state="' + state + '"]').addClass('is-active');
            $detailBox.html('<div class="vas297-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetActivityDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: currentProjId, state: state },
                success: function (response) {
                    if (activeProjState !== state) { return; }
                    var data = parseResponse(response);
                    var items = (data && data.items) || [];
                    if (!items.length) { $detailBox.html('<div class="vas297-state">' + escapeHtml(label('VAS_297_NothingHere', 'Nothing here right now.')) + '</div>'); return; }
                    var rows = items.map(function (a) {
                        return '<div class="vas297-act">' +
                            '<div class="vas297-act-main"><span class="vas297-act-name" title="' + escapeHtml(a.phaseName) + '">' + escapeHtml(a.phaseName) + '</span>' +
                                '<span class="vas297-act-proj" title="' + escapeHtml(a.projectName) + '">' + escapeHtml(a.projectName) + '</span></div>' +
                            '<span class="vas297-act-due">' + escapeHtml(label('VAS_297_Due', 'Due')) + ' ' + escapeHtml(formatDate(a.dueDate)) + '</span>' +
                        '</div>';
                    }).join('');
                    $detailBox.html('<div class="vas297-actlist">' + rows + '</div>');
                },
                error: function () { if (activeProjState === state) { $detailBox.html('<div class="vas297-state">' + escapeHtml(label('VAS_297_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }
        function createProjectsDialog() {
            $proj = $(
                '<div class="vas297-proj-modal" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_297_ActiveProjects', 'Active projects')) + '">' +
                    '<div class="vas297-scrim" data-proj-close></div>' +
                    '<section class="vas297-ppanel">' +
                        '<header class="vas297-phead"><h2 class="vas297-ptitle">' + escapeHtml(label('VAS_297_ActiveProjects', 'Active projects')) + '</h2>' +
                            '<div class="vas297-phead-right"><span class="vas297-psummary"></span>' +
                                '<button type="button" class="vas297-close" data-proj-close aria-label="' + escapeHtml(label('VAS_297_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas297-projbody"></div>' +
                        '<footer class="vas297-dfoot">' +
                            '<button type="button" class="vas297-btn vas297-btn-ghost" data-proj-close>' + escapeHtml(label('VAS_297_CustomerDetails', 'Customer details')) + '</button>' +
                            '<button type="button" class="vas297-btn vas297-btn-primary" data-proj-act="open">' + icon('arrow') + escapeHtml(label('VAS_297_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($proj);
            $projBody = $proj.find('.vas297-projbody');
            $projCust = $proj.find('.vas297-psummary');
            $proj.on('click', '[data-proj-close]', closeProjects);
            $proj.on('click', '[data-proj-act="open"]', function () { var id = currentProjId; closeProjects(); zoomToCustomer(id); });
            $proj.on('click', '.vas297-astat', function () { toggleProjState($(this).attr('data-state')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas297-card">' +
                    '<div class="vas297-head">' +
                        '<div class="vas297-head-l">' +
                            '<span class="vas297-iconwell">' + icon('spark') + '</span>' +
                            '<div class="vas297-head-txt"><div class="vas297-title">' + escapeHtml(label('VAS_297_RecentNewCustomers', 'Recent new customers')) + '</div>' +
                                '<div class="vas297-sub"></div></div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas297-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas297-sub');
            $body = $card.find('.vas297-body');

            $card.on('click', '.vas297-row', function () {
                openCustomer(Number($(this).attr('data-id')), $(this).attr('data-step'));
            });
            $card.on('click', '.vas297-pgbtn', function () { turnPage($(this).attr('data-dir')); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createDetailDialog();
            createProjectsDialog();
            $(document).on('keydown.MPCvas297', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($proj && $proj.hasClass('is-open')) { closeProjects(); }
                else if ($detail && $detail.hasClass('is-open')) { closeDetail(); }
            });
            loadRows();
        };

        this.refreshWidget = function () { pageOffset = 0; loadRows(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas297');
            if ($detail) { $detail.remove(); $detail = null; }
            if ($proj) { $proj.remove(); $proj = null; }
            $('body').removeClass('vas297-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_297_RecentNewCustomersWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_297_RecentNewCustomersWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_297_RecentNewCustomersWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_297_RecentNewCustomersWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_297_RecentNewCustomersWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_297_RecentNewCustomersWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
