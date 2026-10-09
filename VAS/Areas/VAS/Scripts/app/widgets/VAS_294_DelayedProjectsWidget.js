/**
 * VAS_294 Delayed Projects Widget (Customers module dashboard)
 * Purpose - 3x1 compact glass list: customers whose delivery projects have
 *           slipping (delayed) activities, ranked by delayed-activity count
 *           desc; each row names the customer's first delayed project. Row /
 *           View opens the SAME activity-state breakdown modal as VAS_135
 *           Active Projects (five clickable state cells - Delayed / On time /
 *           Ongoing / Upcoming / Completed - plus each project's breakdown,
 *           progress bar, status tag and due date). "All ->" opens the full
 *           server-paged list.
 * Design  - delayed-projects.html (attached) + Design Specs/dashboard-widgets.md
 *           "Grid Data Widget". Glass surface, icon-well header, paged rows (3
 *           - compact 3x1 cell), circular pager. Internal sizing in em against
 *           the widget-root clamp; borders/radii in px. CSS namespaced vas294-*
 *           (MPC prefix rule).
 *
 * Backend - VAS_294_DelayedProjectsWidget/GetList                  (paged customer rows + total)
 *           Projects modal reuses VAS_135's endpoints (same pattern VAS_138
 *           already uses):
 *             VAS_135_ActiveProjectsWidget/GetProjects
 *             VAS_135_ActiveProjectsWidget/GetActivityDetail
 *
 * Routing - hosted on a window, opening the customer navigates the host grid
 *           in place (widgetFirevalueChanged); on the Home / landing dashboard
 *           (windowNo < 0) there is no host grid, so the record is opened in
 *           the standard Customer window via VAS.ZoomUtil. Matches VAS_135.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+----------------------------------+--------------------------------
 *  1  | Delayed projects                | VAS_294_DelayedProjects
 *  2  | clients                         | VAS_294_Clients
 *  3  | with slipping work              | VAS_294_WithSlippingWork
 *  4  | All                             | VAS_294_All
 *  5  | View                            | VAS_294_View
 *  6  | Project                         | VAS_294_ProjectFallback
 *  7  | No owner                        | VAS_294_NoOwner
 *  8  | delayed                         | VAS_294_Delayed
 *  9  | Delayed                         | VAS_294_StateDelayed
 * 10  | On time                         | VAS_294_StateOnTime
 * 11  | Ongoing                         | VAS_294_StateOngoing
 * 12  | Upcoming                        | VAS_294_StateUpcoming
 * 13  | Completed                       | VAS_294_StateCompleted
 * 14  | Due                             | VAS_294_Due
 * 15  | activities                      | VAS_294_Activities
 * 16  | Nothing here right now.         | VAS_294_NothingHere
 * 17  | Unable to load                  | VAS_294_UnableToLoad
 * 18  | of                              | VAS_294_Of
 * 19  | Customers with delayed projects | VAS_294_AllTitle
 * 20  | Close                           | VAS_294_Close
 * 21  | Open record                     | VAS_294_OpenRecord
 * 22  | Showing                         | VAS_294_Showing
 * 23  | Previous page                   | VAS_294_PrevPage
 * 24  | Next page                       | VAS_294_NextPage
 * 25  | Active projects                 | VAS_294_ActiveProjects
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Projects modal reuses VAS_135's endpoints instead of duplicating them
    // (same pattern VAS_138_DelayedPaymentsListWidget already uses).
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

    VAS.VAS_294_DelayedProjectsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas294-root">');
        var $sub, $body;

        // Compact 3x1 cell: 3 rows per page.
        var pageSize = 3;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;

        var $all, $allBody, $allPager, $allCount;
        var allOffset = 0, allTotal = 0, allSeq = 0;
        var ALL_PAGE = 25;

        var $modal, $modalBody, currentModalId = 0, activeState = null;

        var STATES = ['delayed', 'ontime', 'ongoing', 'upcoming', 'completed'];

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

        function icon(name) {
            if (name === 'alert') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m21.73 18-8-14a2 2 0 0 0-3.46 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>';
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

        function stateLabel(state) {
            if (state === 'delayed') { return label('VAS_294_StateDelayed', 'Delayed'); }
            if (state === 'ontime') { return label('VAS_294_StateOnTime', 'On time'); }
            if (state === 'ongoing') { return label('VAS_294_StateOngoing', 'Ongoing'); }
            if (state === 'upcoming') { return label('VAS_294_StateUpcoming', 'Upcoming'); }
            return label('VAS_294_StateCompleted', 'Completed');
        }
        // Status tag colour class per the Onfinity activity palette.
        function statusClass(status) {
            if (status === 'Delayed') { return 'vas294-tag-delayed'; }
            if (status === 'Ongoing') { return 'vas294-tag-ongoing'; }
            if (status === 'Upcoming') { return 'vas294-tag-upcoming'; }
            return 'vas294-tag-ontime';
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_294_DelayedProjectsWidget/GetList',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    listTotal = Number(data.total || 0);
                    $sub.text(formatCount(listTotal) + ' ' + label('VAS_294_Clients', 'clients') + ' ' + label('VAS_294_WithSlippingWork', 'with slipping work'));
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) { $body.html('<div class="vas294-state">' + escapeHtml(message) + '</div>'); }
        function renderErrorState() { $body.html('<div class="vas294-state">' + escapeHtml(label('VAS_294_UnableToLoad', 'Unable to load')) + '</div>'); }

        function rowHtml(customer) {
            var owner = customer.ownerName || label('VAS_294_NoOwner', 'No owner');
            var project = customer.firstDelayedProject || label('VAS_294_ProjectFallback', 'Project');
            var meta = project + ' · ' + owner;
            var delayed = Number(customer.delayedCount || 0);
            return '<div class="vas294-row" data-id="' + Number(customer.customerId) + '">' +
                '<span class="vas294-row-ic">' + icon('alert') + '</span>' +
                '<span class="vas294-row-main">' +
                    '<span class="vas294-row-title" title="' + escapeHtml(customer.customerName) + '">' + escapeHtml(customer.customerName) + '</span>' +
                    '<span class="vas294-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas294-tag vas294-tag-delayed">' + formatCount(delayed) + ' ' + escapeHtml(label('VAS_294_Delayed', 'delayed')) + '</span>' +
                '<button type="button" class="vas294-view" data-id="' + Number(customer.customerId) + '">' + escapeHtml(label('VAS_294_View', 'View')) + '</button>' +
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
            var of = label('VAS_294_Of', 'of');
            var helper = label('VAS_294_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(total);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;
            return '<div class="vas294-pager">' +
                '<span class="vas294-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas294-pgctl">' +
                    '<button type="button" class="vas294-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_294_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas294-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas294-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_294_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) { renderState(label('VAS_294_NothingHere', 'Nothing here right now.')); return; }
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas294-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + items.map(rowHtml).join('') + '</div>' + pagerHtml(pageOffset, pageSize, listTotal));
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
            return ($all && $all.hasClass('is-open')) || ($modal && $modal.hasClass('is-open'));
        }
        function openAll() {
            allOffset = 0;
            $all.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas294-modal-open');
            loadAll();
        }
        function closeAll() {
            if (!$all) { return; }
            $all.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas294-modal-open'); }
        }
        function loadAll() {
            var seq = ++allSeq;
            $allBody.html('<div class="vas294-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $allPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_294_DelayedProjectsWidget/GetList',
                type: 'GET', cache: false,
                data: { offset: allOffset, limit: ALL_PAGE },
                success: function (response) {
                    if (seq !== allSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $allBody.html('<div class="vas294-state">' + escapeHtml(label('VAS_294_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    allTotal = Number(data.total || 0);
                    if ($allCount) { $allCount.text(formatCount(allTotal)); }
                    var items = data.items || [];
                    if (!items.length) { $allBody.html('<div class="vas294-state">' + escapeHtml(label('VAS_294_NothingHere', 'Nothing here right now.')) + '</div>'); return; }
                    $allBody.html('<div class="vas294-list">' + items.map(rowHtml).join('') + '</div>');
                    $allPager.html(pagerHtml(allOffset, ALL_PAGE, allTotal));
                },
                error: function () { if (seq === allSeq) { $allBody.html('<div class="vas294-state">' + escapeHtml(label('VAS_294_UnableToLoad', 'Unable to load')) + '</div>'); } }
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
                '<div class="vas294-dialog" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_294_AllTitle', 'Customers with delayed projects')) + '">' +
                    '<div class="vas294-scrim" data-all-close></div>' +
                    '<section class="vas294-panel">' +
                        '<header class="vas294-phead"><h2 class="vas294-ptitle">' + escapeHtml(label('VAS_294_AllTitle', 'Customers with delayed projects')) + '</h2>' +
                            '<div class="vas294-phead-right"><span class="vas294-pcount"></span>' +
                                '<button type="button" class="vas294-close" data-all-close aria-label="' + escapeHtml(label('VAS_294_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas294-pbody"></div>' +
                        '<footer class="vas294-pfoot"><div class="vas294-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($all);
            $allBody = $all.find('.vas294-pbody');
            $allPager = $all.find('.vas294-pager');
            $allCount = $all.find('.vas294-pcount');
            $all.on('click', '[data-all-close]', closeAll);
            $all.on('click', '.vas294-row, .vas294-view', function (e) { e.stopPropagation(); openProjects(Number($(this).attr('data-id'))); });
            $all.on('click', '.vas294-pgbtn', function () { turnAllPage($(this).attr('data-dir')); });
        }

        /* ---------- Projects modal (reuses VAS_135 project endpoints) ---------- */

        function openProjects(bpId) {
            if (!bpId) { return; }
            currentModalId = bpId;
            activeState = null;
            $modal.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas294-modal-open');
            $modalBody.html('<div class="vas294-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetProjects',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $modalBody.html('<div class="vas294-state">' + escapeHtml(label('VAS_294_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderProjects(data || {});
                },
                error: function () { $modalBody.html('<div class="vas294-state">' + escapeHtml(label('VAS_294_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }
        function closeProjects() {
            if (!$modal) { return; }
            if (document.activeElement && $modal[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $modal.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas294-modal-open'); }
        }

        function statCellHtml(state, count) {
            return '<button type="button" class="vas294-astat MPC-astat-' + state + (activeState === state ? ' is-active' : '') + '" data-state="' + state + '">' +
                '<b>' + formatCount(count) + '</b><span>' + escapeHtml(stateLabel(state)) + '</span></button>';
        }

        function projectRowHtml(project) {
            var total = Number(project.total || 0);
            var progress = Number(project.progressPercent || 0);
            var breakdown = STATES.map(function (s) {
                var n = Number(project[s] || 0);
                return n > 0 ? '<span class="vas294-bd MPC-bd-' + s + '">' + formatCount(n) + ' ' + escapeHtml(stateLabel(s)) + '</span>' : '';
            }).filter(function (x) { return x; }).join('');
            return '<div class="vas294-proj">' +
                '<div class="vas294-proj-head">' +
                    '<span class="vas294-proj-name" title="' + escapeHtml(project.name) + '">' + escapeHtml(project.name) + '</span>' +
                    '<span class="vas294-tag ' + statusClass(project.status) + '">' + escapeHtml(project.status) + '</span>' +
                '</div>' +
                (breakdown ? '<div class="vas294-proj-bd">' + breakdown + '</div>' : '') +
                '<div class="vas294-bar"><div style="width:' + Math.max(0, Math.min(100, progress)) + '%"></div></div>' +
                '<div class="vas294-proj-foot">' +
                    '<span>' + formatCount(project.completed) + '/' + formatCount(total) + ' ' + escapeHtml(label('VAS_294_Activities', 'activities')) + ' · ' + progress + '%</span>' +
                    '<span>' + escapeHtml(label('VAS_294_Due', 'Due')) + ' ' + escapeHtml(formatDate(project.dueDate)) + '</span>' +
                '</div>' +
            '</div>';
        }

        function renderProjects(data) {
            var summary = data.summary || {};
            var projects = data.projects || [];
            var cells = STATES.map(function (s) { return statCellHtml(s, summary[s] || 0); }).join('');
            var projRows = projects.length
                ? projects.map(projectRowHtml).join('')
                : '<div class="vas294-state">' + escapeHtml(label('VAS_294_NothingHere', 'Nothing here right now.')) + '</div>';
            $modalBody.html(
                '<div class="vas294-statgrid">' + cells + '</div>' +
                '<div class="vas294-actdetail"></div>' +
                '<div class="vas294-projects">' + projRows + '</div>'
            );
        }

        function toggleState(state) {
            var $detail = $modal.find('.vas294-actdetail');
            if (activeState === state) {
                activeState = null;
                $modal.find('.vas294-astat').removeClass('is-active');
                $detail.empty();
                return;
            }
            activeState = state;
            $modal.find('.vas294-astat').removeClass('is-active').filter('[data-state="' + state + '"]').addClass('is-active');
            $detail.html('<div class="vas294-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetActivityDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: currentModalId, state: state },
                success: function (response) {
                    if (activeState !== state) { return; }
                    var data = parseResponse(response);
                    var items = (data && data.items) || [];
                    if (!items.length) { $detail.html('<div class="vas294-state">' + escapeHtml(label('VAS_294_NothingHere', 'Nothing here right now.')) + '</div>'); return; }
                    var rows = items.map(function (a) {
                        return '<div class="vas294-act">' +
                            '<div class="vas294-act-main"><span class="vas294-act-name" title="' + escapeHtml(a.phaseName) + '">' + escapeHtml(a.phaseName) + '</span>' +
                                '<span class="vas294-act-proj" title="' + escapeHtml(a.projectName) + '">' + escapeHtml(a.projectName) + '</span></div>' +
                            '<span class="vas294-act-due">' + escapeHtml(label('VAS_294_Due', 'Due')) + ' ' + escapeHtml(formatDate(a.dueDate)) + '</span>' +
                        '</div>';
                    }).join('');
                    $detail.html('<div class="vas294-actlist">' + rows + '</div>');
                },
                error: function () { if (activeState === state) { $detail.html('<div class="vas294-state">' + escapeHtml(label('VAS_294_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }

        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            closeProjects();
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

        function createProjectsDialog() {
            $modal = $(
                '<div class="vas294-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_294_ActiveProjects', 'Active projects')) + '">' +
                    '<div class="vas294-scrim" data-modal-close></div>' +
                    '<section class="vas294-dpanel">' +
                        '<header class="vas294-phead"><h2 class="vas294-ptitle">' + escapeHtml(label('VAS_294_ActiveProjects', 'Active projects')) + '</h2>' +
                            '<button type="button" class="vas294-close" data-modal-close aria-label="' + escapeHtml(label('VAS_294_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas294-dbody"></div>' +
                        '<footer class="vas294-dfoot">' +
                            '<button type="button" class="vas294-btn vas294-btn-primary" data-modal-act="open">' + icon('arrow') + escapeHtml(label('VAS_294_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($modal);
            $modalBody = $modal.find('.vas294-dbody');
            $modal.on('click', '[data-modal-close]', closeProjects);
            $modal.on('click', '[data-modal-act="open"]', function () { zoomToCustomer(currentModalId); });
            $modal.on('click', '.vas294-astat', function () { toggleState($(this).attr('data-state')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas294-card">' +
                    '<div class="vas294-head">' +
                        '<div class="vas294-head-l">' +
                            '<span class="vas294-iconwell">' + icon('alert') + '</span>' +
                            '<div class="vas294-head-txt"><div class="vas294-title">' + escapeHtml(label('VAS_294_DelayedProjects', 'Delayed projects')) + '</div>' +
                                '<div class="vas294-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas294-alllink">' + escapeHtml(label('VAS_294_All', 'All')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas294-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas294-sub');
            $body = $card.find('.vas294-body');
            $card.on('click', '.vas294-alllink', function () { openAll(); });
            $card.on('click', '.vas294-view', function (e) { e.stopPropagation(); openProjects(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas294-row', function () { openProjects(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas294-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createAllDialog();
            createProjectsDialog();
            $(document).on('keydown.MPCvas294', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($modal && $modal.hasClass('is-open')) { closeProjects(); }
                else if ($all && $all.hasClass('is-open')) { closeAll(); }
            });
            loadRows();
        };

        this.refreshWidget = function () { pageOffset = 0; loadRows(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas294');
            if ($all) { $all.remove(); $all = null; }
            if ($modal) { $modal.remove(); $modal = null; }
            $('body').removeClass('vas294-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_294_DelayedProjectsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_294_DelayedProjectsWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_294_DelayedProjectsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_294_DelayedProjectsWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_294_DelayedProjectsWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_294_DelayedProjectsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
