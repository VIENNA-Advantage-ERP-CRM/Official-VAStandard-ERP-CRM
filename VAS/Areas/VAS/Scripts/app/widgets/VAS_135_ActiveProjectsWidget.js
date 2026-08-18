/**
 * VAS_135 Active Projects Widget (Customers module dashboard)
 * Purpose - 3x2 glass list: customers with active delivery projects
 *           (C_Project.VAS_ProjectStatus='IP'), clients with delays ranked first.
 *           Row / View opens the project modal: five clickable activity-state cells
 *           (Delayed / On time / Ongoing / Upcoming / Completed) derived from the
 *           project phases, plus each project with its activity breakdown, progress
 *           bar, status tag and due date. "All ->" opens the full server-paged list.
 * Design  - active-projects.html (attached) + Design Specs/dashboard-widgets.md
 *           "Grid Data Widget". Glass surface, icon-well header, paged rows (5),
 *           circular pager, activity-state stat cells. Internal sizing in em against
 *           the widget-root clamp; borders/radii in px. CSS namespaced vas135-*.
 *
 * Backend - VAS_135_ActiveProjectsWidget/GetList            (paged customer rows + totals)
 *           VAS_135_ActiveProjectsWidget/GetProjects        (one customer's projects)
 *           VAS_135_ActiveProjectsWidget/GetActivityDetail  (phases in one state)
 *
 * Routing - 2026-08-17: hosted on a window, opening the customer navigates the host
 *           grid in place (widgetFirevalueChanged); on the Home / landing dashboard
 *           (windowNo < 0) there is no host grid, so the record is opened in the
 *           standard Customer window via VAS.ZoomUtil.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text            | Message Key
 * ----+-------------------------+--------------------------------
 *  1  | Active projects         | VAS_135_ActiveProjects
 *  2  | clients                 | VAS_135_Clients
 *  3  | with delays             | VAS_135_WithDelays
 *  4  | All                     | VAS_135_All
 *  5  | View                    | VAS_135_View
 *  6  | project                 | VAS_135_Project
 *  7  | projects                | VAS_135_Projects
 *  8  | No owner                | VAS_135_NoOwner
 *  9  | delayed                 | VAS_135_Delayed
 * 10  | On track                | VAS_135_OnTrack
 * 11  | Delayed                 | VAS_135_StateDelayed
 * 12  | On time                 | VAS_135_StateOnTime
 * 13  | Ongoing                 | VAS_135_StateOngoing
 * 14  | Upcoming                | VAS_135_StateUpcoming
 * 15  | Completed               | VAS_135_StateCompleted
 * 16  | Due                     | VAS_135_Due
 * 17  | activities              | VAS_135_Activities
 * 18  | Nothing here right now. | VAS_135_NothingHere
 * 19  | Unable to load          | VAS_135_UnableToLoad
 * 20  | of                      | VAS_135_Of
 * 21  | Customers with active projects | VAS_135_AllTitle
 * 22  | Close                   | VAS_135_Close
 * 23  | Open record             | VAS_135_OpenRecord
 * 24  | Showing                 | VAS_135_Showing
 * 25  | Previous page           | VAS_135_PrevPage
 * 26  | Next page               | VAS_135_NextPage
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var CUSTOMER_WINDOW_NAME = 'Business Partner';

    /* 2026-08-17: zoom target when the widget is NOT hosted inside a window
       (windowNo < 0 - the Home / landing dashboard). There is no host grid to navigate
       there, so the record is opened in the standard Customer window; VAS.ZoomUtil
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

    VAS.VAS_135_ActiveProjectsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas135-root">');
        var $sub, $body;

        // 5 rows per page (was 3) — the card is tall enough for five and the body
        // grid below spreads them through its full height. Matches VAS_127.
        var pageSize = 5;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;

        var $all, $allBody, $allPager, $allCount;
        var allOffset = 0, allTotal = 0, allSeq = 0;
        var ALL_PAGE = 25;

        var $modal, $modalBody, $modalTitle, currentModalId = 0, activeState = null;

        var STATES = ['delayed', 'ontime', 'ongoing', 'upcoming', 'completed'];

        // AD_Window_ID of the Customer window, resolved once on the first Home-page
        // zoom and reused afterwards (0 = not resolved yet).
        var zoomWindowId = 0;

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
        function formatDate(iso) {
            if (!iso) { return '—'; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return iso; }
            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }

        function icon(name) {
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

        function stateLabel(state) {
            if (state === 'delayed') { return label('VAS_135_StateDelayed', 'Delayed'); }
            if (state === 'ontime') { return label('VAS_135_StateOnTime', 'On time'); }
            if (state === 'ongoing') { return label('VAS_135_StateOngoing', 'Ongoing'); }
            if (state === 'upcoming') { return label('VAS_135_StateUpcoming', 'Upcoming'); }
            return label('VAS_135_StateCompleted', 'Completed');
        }
        // Status tag colour class per the Onfinity activity palette.
        function statusClass(status) {
            if (status === 'Delayed') { return 'vas135-tag-delayed'; }
            if (status === 'Ongoing') { return 'vas135-tag-ongoing'; }
            if (status === 'Upcoming') { return 'vas135-tag-upcoming'; }
            return 'vas135-tag-ontime';
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_135_ActiveProjectsWidget/GetList',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    listTotal = Number(data.total || 0);
                    var withDelays = Number(data.customersWithDelays || 0);
                    $sub.text(formatCount(listTotal) + ' ' + label('VAS_135_Clients', 'clients') + ' · ' + formatCount(withDelays) + ' ' + label('VAS_135_WithDelays', 'with delays'));
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) { $body.html('<div class="vas135-state">' + escapeHtml(message) + '</div>'); }
        function renderErrorState() { $body.html('<div class="vas135-state">' + escapeHtml(label('VAS_135_UnableToLoad', 'Unable to load')) + '</div>'); }

        function rowHtml(customer) {
            var count = Number(customer.projectCount || 0);
            var projWord = count === 1 ? label('VAS_135_Project', 'project') : label('VAS_135_Projects', 'projects');
            var owner = customer.ownerName || label('VAS_135_NoOwner', 'No owner');
            var meta = formatCount(count) + ' ' + projWord + ' · ' + owner;
            var delayed = Number(customer.delayedCount || 0);
            var statusTag = delayed > 0
                ? '<span class="vas135-tag vas135-tag-delayed">' + formatCount(delayed) + ' ' + escapeHtml(label('VAS_135_Delayed', 'delayed')) + '</span>'
                : '<span class="vas135-tag vas135-tag-ontime">' + escapeHtml(label('VAS_135_OnTrack', 'On track')) + '</span>';
            return '<div class="vas135-row" data-id="' + Number(customer.customerId) + '">' +
                '<span class="vas135-row-ic">' + icon('folder') + '</span>' +
                '<span class="vas135-row-main">' +
                    '<span class="vas135-row-title" title="' + escapeHtml(customer.customerName) + '">' + escapeHtml(customer.customerName) + '</span>' +
                    '<span class="vas135-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                statusTag +
                '<button type="button" class="vas135-view" data-id="' + Number(customer.customerId) + '">' + escapeHtml(label('VAS_135_View', 'View')) + '</button>' +
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
            var of = label('VAS_135_Of', 'of');
            var helper = label('VAS_135_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(total);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;
            return '<div class="vas135-pager">' +
                '<span class="vas135-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas135-pgctl">' +
                    '<button type="button" class="vas135-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_135_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas135-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas135-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_135_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) { renderState(label('VAS_135_NothingHere', 'Nothing here right now.')); return; }
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas135-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + items.map(rowHtml).join('') + '</div>' + pagerHtml(pageOffset, pageSize, listTotal));
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
            $('body').addClass('vas135-modal-open');
            loadAll();
        }
        function closeAll() {
            if (!$all) { return; }
            $all.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas135-modal-open'); }
        }
        function loadAll() {
            var seq = ++allSeq;
            $allBody.html('<div class="vas135-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $allPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_135_ActiveProjectsWidget/GetList',
                type: 'GET', cache: false,
                data: { offset: allOffset, limit: ALL_PAGE },
                success: function (response) {
                    if (seq !== allSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $allBody.html('<div class="vas135-state">' + escapeHtml(label('VAS_135_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    allTotal = Number(data.total || 0);
                    if ($allCount) { $allCount.text(formatCount(allTotal)); }
                    var items = data.items || [];
                    if (!items.length) { $allBody.html('<div class="vas135-state">' + escapeHtml(label('VAS_135_NothingHere', 'Nothing here right now.')) + '</div>'); return; }
                    $allBody.html('<div class="vas135-list">' + items.map(rowHtml).join('') + '</div>');
                    $allPager.html(pagerHtml(allOffset, ALL_PAGE, allTotal));
                },
                error: function () { if (seq === allSeq) { $allBody.html('<div class="vas135-state">' + escapeHtml(label('VAS_135_UnableToLoad', 'Unable to load')) + '</div>'); } }
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
                '<div class="vas135-dialog" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_135_AllTitle', 'Customers with active projects')) + '">' +
                    '<div class="vas135-scrim" data-all-close></div>' +
                    '<section class="vas135-panel">' +
                        '<header class="vas135-phead"><h2 class="vas135-ptitle">' + escapeHtml(label('VAS_135_AllTitle', 'Customers with active projects')) + '</h2>' +
                            '<div class="vas135-phead-right"><span class="vas135-pcount"></span>' +
                                '<button type="button" class="vas135-close" data-all-close aria-label="' + escapeHtml(label('VAS_135_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas135-pbody"></div>' +
                        '<footer class="vas135-pfoot"><div class="vas135-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($all);
            $allBody = $all.find('.vas135-pbody');
            $allPager = $all.find('.vas135-pager');
            $allCount = $all.find('.vas135-pcount');
            $all.on('click', '[data-all-close]', closeAll);
            $all.on('click', '.vas135-row, .vas135-view', function (e) { e.stopPropagation(); openProjects(Number($(this).attr('data-id'))); });
            $all.on('click', '.vas135-pgbtn', function () { turnAllPage($(this).attr('data-dir')); });
        }

        /* ---------- Projects modal ---------- */

        function openProjects(bpId) {
            if (!bpId) { return; }
            currentModalId = bpId;
            activeState = null;
            $modal.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas135-modal-open');
            $modalBody.html('<div class="vas135-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_135_ActiveProjectsWidget/GetProjects',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $modalBody.html('<div class="vas135-state">' + escapeHtml(label('VAS_135_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderProjects(data || {});
                },
                error: function () { $modalBody.html('<div class="vas135-state">' + escapeHtml(label('VAS_135_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }
        function closeProjects() {
            if (!$modal) { return; }
            if (document.activeElement && $modal[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $modal.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas135-modal-open'); }
        }

        function statCellHtml(state, count) {
            return '<button type="button" class="vas135-astat MPC-astat-' + state + (activeState === state ? ' is-active' : '') + '" data-state="' + state + '">' +
                '<b>' + formatCount(count) + '</b><span>' + escapeHtml(stateLabel(state)) + '</span></button>';
        }

        function projectRowHtml(project) {
            var total = Number(project.total || 0);
            var progress = Number(project.progressPercent || 0);
            var breakdown = STATES.map(function (s) {
                var n = Number(project[s] || 0);
                return n > 0 ? '<span class="vas135-bd MPC-bd-' + s + '">' + formatCount(n) + ' ' + escapeHtml(stateLabel(s)) + '</span>' : '';
            }).filter(function (x) { return x; }).join('');
            return '<div class="vas135-proj">' +
                '<div class="vas135-proj-head">' +
                    '<span class="vas135-proj-name" title="' + escapeHtml(project.name) + '">' + escapeHtml(project.name) + '</span>' +
                    '<span class="vas135-tag ' + statusClass(project.status) + '">' + escapeHtml(project.status) + '</span>' +
                '</div>' +
                (breakdown ? '<div class="vas135-proj-bd">' + breakdown + '</div>' : '') +
                '<div class="vas135-bar"><div style="width:' + Math.max(0, Math.min(100, progress)) + '%"></div></div>' +
                '<div class="vas135-proj-foot">' +
                    '<span>' + formatCount(project.completed) + '/' + formatCount(total) + ' ' + escapeHtml(label('VAS_135_Activities', 'activities')) + ' · ' + progress + '%</span>' +
                    '<span>' + escapeHtml(label('VAS_135_Due', 'Due')) + ' ' + escapeHtml(formatDate(project.dueDate)) + '</span>' +
                '</div>' +
            '</div>';
        }

        function renderProjects(data) {
            var summary = data.summary || {};
            var projects = data.projects || [];
            var cells = STATES.map(function (s) { return statCellHtml(s, summary[s] || 0); }).join('');
            var projRows = projects.length
                ? projects.map(projectRowHtml).join('')
                : '<div class="vas135-state">' + escapeHtml(label('VAS_135_NothingHere', 'Nothing here right now.')) + '</div>';
            $modalBody.html(
                '<div class="vas135-statgrid">' + cells + '</div>' +
                '<div class="vas135-actdetail"></div>' +
                '<div class="vas135-projects">' + projRows + '</div>'
            );
        }

        function toggleState(state) {
            var $detail = $modal.find('.vas135-actdetail');
            if (activeState === state) {
                activeState = null;
                $modal.find('.vas135-astat').removeClass('is-active');
                $detail.empty();
                return;
            }
            activeState = state;
            $modal.find('.vas135-astat').removeClass('is-active').filter('[data-state="' + state + '"]').addClass('is-active');
            $detail.html('<div class="vas135-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_135_ActiveProjectsWidget/GetActivityDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: currentModalId, state: state },
                success: function (response) {
                    if (activeState !== state) { return; }
                    var data = parseResponse(response);
                    var items = (data && data.items) || [];
                    if (!items.length) { $detail.html('<div class="vas135-state">' + escapeHtml(label('VAS_135_NothingHere', 'Nothing here right now.')) + '</div>'); return; }
                    var rows = items.map(function (a) {
                        return '<div class="vas135-act">' +
                            '<div class="vas135-act-main"><span class="vas135-act-name" title="' + escapeHtml(a.phaseName) + '">' + escapeHtml(a.phaseName) + '</span>' +
                                '<span class="vas135-act-proj" title="' + escapeHtml(a.projectName) + '">' + escapeHtml(a.projectName) + '</span></div>' +
                            '<span class="vas135-act-due">' + escapeHtml(label('VAS_135_Due', 'Due')) + ' ' + escapeHtml(formatDate(a.dueDate)) + '</span>' +
                        '</div>';
                    }).join('');
                    $detail.html('<div class="vas135-actlist">' + rows + '</div>');
                },
                error: function () { if (activeState === state) { $detail.html('<div class="vas135-state">' + escapeHtml(label('VAS_135_UnableToLoad', 'Unable to load')) + '</div>'); } }
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
                '<div class="vas135-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_135_ActiveProjects', 'Active projects')) + '">' +
                    '<div class="vas135-scrim" data-modal-close></div>' +
                    '<section class="vas135-dpanel">' +
                        '<header class="vas135-phead"><h2 class="vas135-ptitle">' + escapeHtml(label('VAS_135_ActiveProjects', 'Active projects')) + '</h2>' +
                            '<button type="button" class="vas135-close" data-modal-close aria-label="' + escapeHtml(label('VAS_135_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas135-dbody"></div>' +
                        '<footer class="vas135-dfoot">' +
                            '<button type="button" class="vas135-btn vas135-btn-primary" data-modal-act="open">' + icon('arrow') + escapeHtml(label('VAS_135_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($modal);
            $modalBody = $modal.find('.vas135-dbody');
            $modal.on('click', '[data-modal-close]', closeProjects);
            $modal.on('click', '[data-modal-act="open"]', function () { zoomToCustomer(currentModalId); });
            $modal.on('click', '.vas135-astat', function () { toggleState($(this).attr('data-state')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas135-card">' +
                    '<div class="vas135-head">' +
                        '<div class="vas135-head-l">' +
                            '<span class="vas135-iconwell">' + icon('folder') + '</span>' +
                            '<div class="vas135-head-txt"><div class="vas135-title">' + escapeHtml(label('VAS_135_ActiveProjects', 'Active projects')) + '</div>' +
                                '<div class="vas135-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas135-alllink">' + escapeHtml(label('VAS_135_All', 'All')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas135-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas135-sub');
            $body = $card.find('.vas135-body');
            $card.on('click', '.vas135-alllink', function () { openAll(); });
            $card.on('click', '.vas135-view', function (e) { e.stopPropagation(); openProjects(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas135-row', function () { openProjects(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas135-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createAllDialog();
            createProjectsDialog();
            $(document).on('keydown.MPCvas135', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($modal && $modal.hasClass('is-open')) { closeProjects(); }
                else if ($all && $all.hasClass('is-open')) { closeAll(); }
            });
            loadRows();
        };

        this.refreshWidget = function () { pageOffset = 0; loadRows(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas135');
            if ($all) { $all.remove(); $all = null; }
            if ($modal) { $modal.remove(); $modal = null; }
            $('body').removeClass('vas135-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_135_ActiveProjectsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_135_ActiveProjectsWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_135_ActiveProjectsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_135_ActiveProjectsWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_135_ActiveProjectsWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_135_ActiveProjectsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
