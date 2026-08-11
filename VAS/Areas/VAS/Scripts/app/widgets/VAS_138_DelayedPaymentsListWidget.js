/**
 * VAS_138 Delayed Payments List Widget (Customers module dashboard)
 * Purpose - 3x2 glass triage list: customers with overdue receivable payment
 *           schedules, ranked by outstanding amount. Header shows
 *           "{clients} clients · {total} overdue" and an "All ->" link that opens
 *           the full server-paged list. Each row shows the customer, the invoice
 *           number, days overdue and the outstanding amount (danger red) with an
 *           "Open" action. Row / Open opens the customer detail modal.
 * Design  - delayed-payments.html (attached) + Design Specs/dashboard-widgets.md
 *           "Grid Data Widget". Glass surface, icon-well header, paged rows (7),
 *           circular pager. Internal sizing in em against the widget-root clamp;
 *           borders/radii in px. CSS namespaced vas138-* (MPC prefix rule).
 *
 * Backend - VAS_138_DelayedPaymentsListWidget/GetSummary
 *           VAS_138_DelayedPaymentsListWidget/GetRows          (paged ranked rows)
 *           Customer detail reuses the generic VAS_126 endpoint:
 *             VAS_126_OpenTicketsWidget/GetCustomerDetail
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                       | Message Key
 * ----+------------------------------------+--------------------------------
 *  1  | Delayed payments                   | VAS_138_DelayedPayments
 *  2  | clients                            | VAS_138_Clients
 *  3  | client                             | VAS_138_Client
 *  4  | overdue                            | VAS_138_Overdue
 *  5  | All                                | VAS_138_All
 *  6  | Open                               | VAS_138_Open
 *  7  | of                                 | VAS_138_Of
 * 7a  | Showing                            | VAS_138_Showing
 * 7b  | Previous page                      | VAS_138_PrevPage
 * 7c  | Next page                          | VAS_138_NextPage
 *  8  | Nothing here right now.            | VAS_138_NothingHere
 *  9  | Unable to load delayed payments.   | VAS_138_UnableToLoad
 * 10  | Retry                              | VAS_138_Retry
 * 11  | Overdue receivables                | VAS_138_AllTitle
 * 12  | Close                              | VAS_138_Close
 * 13  | Customer details                   | VAS_138_CustomerDetails
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Generic customer detail / activity-log endpoints (built with VAS_126).
    var CUSTOMER_ENDPOINT = 'VAS_126_OpenTicketsWidget/';
    // Customer projects endpoints (built with VAS_135).
    var PROJECT_ENDPOINT = 'VAS_135_ActiveProjectsWidget/';
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

    VAS.VAS_138_DelayedPaymentsListWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas138-root">');
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

        // Detail + log modal state (reuses VAS_126 generic endpoints).
        var $detail, $detailBody, $detailSummary, currentDetailId = 0, currentDetailName = '';
        var detailCurrency = { symbol: '', iso: '', precision: 2 };

        // Projects modal state (reuses VAS_135 project endpoints).
        var $proj, $projBody, $projCust, currentProjId = 0, activeProjState = null;
        var PROJECT_STATES = ['delayed', 'ontime', 'ongoing', 'upcoming', 'completed'];

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

        // Projects fact: name the customer's delivery project rather than only
        // counting it. The server sends the first project name plus the total, so
        // a customer with several reads "Name +2"; none renders the empty dash.
        function projectText(name, count, dash) {
            if (!name) { return dash; }
            var extra = Number(count || 0) - 1;
            return escapeHtml(extra > 0 ? name + ' +' + formatCount(extra) : name);
        }

        function formatCount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return Math.round(n).toLocaleString(window.navigator.language);
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

        // Compact money against the base (accounting-schema) currency the endpoint
        // reported. VIS.Util.formatCompactAmount supplies the scaled magnitude at the
        // system-configured precision; the sign is composed before the symbol here.
        function formatMoney(value, cur) {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '';
            var iso = (cur && cur.iso) || '';
            var symbol = (cur && (cur.symbol || cur.iso)) || '';
            return sign + symbol + VIS.Util.formatCompactAmount(n, iso, precisionOf(cur));
        }

        function icon(name) {
            if (name === 'cash') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"></rect><circle cx="12" cy="12" r="2"></circle><path d="M6 12h.01M18 12h.01"></path></svg>';
            }
            if (name === 'ticket') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v1a2 2 0 0 0 0 4v1a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-1a2 2 0 0 0 0-4z"></path><line x1="9" y1="7" x2="9" y2="17"></line></svg>';
            }
            if (name === 'folder') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.93a2 2 0 0 1-1.66-.9l-.82-1.2A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13c0 1.1.9 2 2 2z"></path></svg>';
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
            var cls = 'vas138-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas138-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas138-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas138-tag-info'; }
            return '<span class="vas138-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }

        /* ---------- Summary header ---------- */

        function loadSummary() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_138_DelayedPaymentsListWidget/GetSummary',
                type: 'GET', cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { return; }
                    widgetCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
                    var clients = Number(data.overdue_customer_count || 0);
                    var clientWord = clients === 1 ? label('VAS_138_Client', 'client') : label('VAS_138_Clients', 'clients');
                    var total = Number(data.overdue_amount || 0);
                    $sub.text(formatCount(clients) + ' ' + clientWord + ' · ' + formatMoney(total, widgetCurrency) + ' ' + label('VAS_138_Overdue', 'overdue'));
                },
                error: function () { /* header subtitle stays blank on summary error */ }
            });
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_138_DelayedPaymentsListWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(data.error); return; }
                    listTotal = Number(data.total || 0);
                    widgetCurrency = { symbol: data.currency_symbol || widgetCurrency.symbol, iso: data.currency_iso || widgetCurrency.iso, precision: data.std_precision };
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) {
            $body.html('<div class="vas138-state">' + escapeHtml(message) + '</div>');
        }

        function renderErrorState(detail) {
            var extra = detail ? '<div class="vas138-errdetail">' + escapeHtml(String(detail)) + '</div>' : '';
            $body.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_UnableToLoad', 'Unable to load delayed payments.')) +
                ' <button type="button" class="vas138-retry">' + escapeHtml(label('VAS_138_Retry', 'Retry')) + '</button>' + extra + '</div>');
        }

        // One row per customer in arrears: "N invoices · Md overdue" (the worst age
        // across that customer's overdue AR invoices).
        function rowHtml(item, cur) {
            var days = Number(item.overdueDays || 0);
            var invoices = Number(item.invoiceCount || 0);
            var invoiceWord = invoices === 1
                ? label('VAS_138_Invoice', 'invoice')
                : label('VAS_138_Invoices', 'invoices');
            var meta = [
                invoices > 0 ? formatCount(invoices) + ' ' + invoiceWord : '',
                formatCount(days) + 'd ' + label('VAS_138_Overdue', 'overdue')
            ].filter(function (p) { return p; }).join(' · ');
            return '<div class="vas138-row" data-id="' + Number(item.customerId) + '">' +
                '<span class="vas138-row-ic">' + icon('cash') + '</span>' +
                '<span class="vas138-row-main">' +
                    '<span class="vas138-row-title" title="' + escapeHtml(item.customerName) + '">' + escapeHtml(item.customerName) + '</span>' +
                    '<span class="vas138-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas138-row-amt">' +
                    '<span class="vas138-amt-val">' + escapeHtml(formatMoney(item.overdueAmt, cur)) + '</span>' +
                    '<span class="vas138-amt-lab">' + escapeHtml(label('VAS_138_Overdue', 'overdue')) + '</span>' +
                '</span>' +
                '<button type="button" class="vas138-open" data-id="' + Number(item.customerId) + '">' + escapeHtml(label('VAS_138_Open', 'Open')) + '</button>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) {
                renderState(label('VAS_138_NothingHere', 'Nothing here right now.'));
                return;
            }
            var cur = widgetCurrency;
            var rows = items.map(function (it) { return rowHtml(it, cur); }).join('');
            var start = pageOffset + 1;
            var end = pageOffset + items.length;
            var pages = Math.max(1, Math.ceil(listTotal / pageSize));
            var current = Math.floor(pageOffset / pageSize);
            /* Footer pager (dashboard-widgets.md §"Widget Footer Pager"): helper
               left, compact prev · "N of M" · next right. The control stays put
               on a single page with both arrows disabled, so the widget's row
               grid never shifts height between pages. */
            var of = label('VAS_138_Of', 'of');
            var helper = label('VAS_138_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listTotal);
            var pager = '<div class="vas138-pager">' +
                '<span class="vas138-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas138-pgctl">' +
                    '<button type="button" class="vas138-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_138_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas138-pgtext">' + escapeHtml((current + 1) + ' ' + of + ' ' + pages) + '</span>' +
                    '<button type="button" class="vas138-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_138_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>' +
            '</div>';
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas138-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + rows + '</div>' + pager);
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
            $all.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas138-modal-open');
            loadAll();
        }
        function closeAll() {
            if (!$all) { return; }
            $all.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas138-modal-open'); }
        }
        function loadAll() {
            var seq = ++allSeq;
            $allBody.html('<div class="vas138-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $allPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_138_DelayedPaymentsListWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: allOffset, limit: ALL_PAGE },
                success: function (response) {
                    if (seq !== allSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $allBody.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_UnableToLoad', 'Unable to load delayed payments.')) + '</div>'); return; }
                    allTotal = Number(data.total || 0);
                    if ($allCount) { $allCount.text(formatCount(allTotal)); }
                    var cur = { symbol: data.currency_symbol || widgetCurrency.symbol, iso: data.currency_iso || widgetCurrency.iso, precision: data.std_precision };
                    renderAll(data.items || [], cur);
                },
                error: function () { if (seq === allSeq) { $allBody.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_UnableToLoad', 'Unable to load delayed payments.')) + '</div>'); } }
            });
        }
        function renderAll(items, cur) {
            if (!items.length) {
                $allBody.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_NothingHere', 'Nothing here right now.')) + '</div>');
                $allPager.empty();
                return;
            }
            $allBody.html('<div class="vas138-list">' + items.map(function (it) { return rowHtml(it, cur); }).join('') + '</div>');
            var start = allOffset + 1, end = allOffset + items.length;
            var pages = Math.max(1, Math.ceil(allTotal / ALL_PAGE));
            var current = Math.floor(allOffset / ALL_PAGE);
            var allOf = label('VAS_138_Of', 'of');
            $allPager.html(
                '<span class="vas138-pglabel">' + escapeHtml(label('VAS_138_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + allOf + ' ' + formatCount(allTotal)) + '</span>' +
                '<span class="vas138-pgctl">' +
                    '<button type="button" class="vas138-pgbtn" data-alldir="prev" aria-label="' + escapeHtml(label('VAS_138_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas138-pgtext">' + escapeHtml((current + 1) + ' ' + allOf + ' ' + pages) + '</span>' +
                    '<button type="button" class="vas138-pgbtn" data-alldir="next" aria-label="' + escapeHtml(label('VAS_138_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>'
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
                '<div class="vas138-dialog" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_138_AllTitle', 'Overdue receivables')) + '">' +
                    '<div class="vas138-scrim" data-all-close></div>' +
                    '<section class="vas138-panel">' +
                        '<header class="vas138-phead">' +
                            '<h2 class="vas138-ptitle">' + escapeHtml(label('VAS_138_AllTitle', 'Overdue receivables')) + '</h2>' +
                            '<div class="vas138-phead-right"><span class="vas138-pcount"></span>' +
                                '<button type="button" class="vas138-close" data-all-close aria-label="' + escapeHtml(label('VAS_138_Close', 'Close')) + '">' + icon('close') + '</button></div>' +
                        '</header>' +
                        '<div class="vas138-pbody"></div>' +
                        '<footer class="vas138-pfoot"><div class="vas138-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($all);
            $allBody = $all.find('.vas138-pbody');
            $allPager = $all.find('.vas138-pager');
            $allCount = $all.find('.vas138-pcount');

            $all.on('click', '[data-all-close]', closeAll);
            $all.on('click', '.vas138-row, .vas138-open', function (e) { e.stopPropagation(); openCustomer(Number($(this).attr('data-id'))); });
            $all.on('click', '.vas138-pgbtn', function () { turnAllPage($(this).attr('data-alldir')); });
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

        function anyModalOpen() {
            return ($all && $all.hasClass('is-open')) || ($detail && $detail.hasClass('is-open')) ||
                ($proj && $proj.hasClass('is-open'));
        }

        function openCustomer(bpId) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas138-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas138-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_UnableToLoad', 'Unable to load delayed payments.')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_UnableToLoad', 'Unable to load delayed payments.')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas138-fact"><div class="vas138-fl">' + escapeHtml(fallback) + '</div><div class="vas138-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas138-signal"><span class="vas138-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas138-sig-main"><div class="vas138-sig-name">' + titleHtml + '</div><div class="vas138-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
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
            if (data.isKeyClient) { summaryParts.push(label('VAS_138_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas138-factgrid">' +
                fact(label('VAS_138_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_138_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_138_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_138_ARR', 'ARR'), escapeHtml(formatMoney(data.value, detailCurrency))) +
                fact(label('VAS_138_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_138_Projects', 'Projects'), projectText(data.projectName, projects, dash)) +
                fact(label('VAS_138_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatMoney(pipeline, detailCurrency)) : dash) +
                fact(label('VAS_138_Onboarding', 'Onboarding'), data.onboardingPercent == null ? dash : escapeHtml(formatCount(data.onboardingPercent) + '%')) +
            '</div>';

            var signals = '';
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatMoney(overdue, detailCurrency) + ' ' + label('VAS_138_Overdue', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_138_DaysPastDue', 'days past due'));
            }
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_138_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_138_KeyPrioritise', 'Key client — prioritise') : label('VAS_138_OpenRequests', 'Open support requests'));
            }
            var signalsBlock = signals ? '<div class="vas138-signals"><div class="vas138-sig-title">' + escapeHtml(label('VAS_138_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas138-id">' +
                '<span class="vas138-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas138-id-main"><div class="vas138-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas138-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas138-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas138-modal-open'); }
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
                '<div class="vas138-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_138_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas138-scrim" data-detail-close></div>' +
                    '<section class="vas138-dpanel">' +
                        '<header class="vas138-phead"><h2 class="vas138-ptitle">' + escapeHtml(label('VAS_138_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas138-phead-right"><span class="vas138-dsummary"></span>' +
                                '<button type="button" class="vas138-close" data-detail-close aria-label="' + escapeHtml(label('VAS_138_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas138-dbody"></div>' +
                        '<footer class="vas138-dfoot">' +
                            '<button type="button" class="vas138-btn vas138-btn-ghost" data-detail-act="projects">' + icon('folder') + escapeHtml(label('VAS_138_Projects', 'Projects')) + '</button>' +
                            '<button type="button" class="vas138-btn vas138-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_138_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas138-dbody');
            $detailSummary = $detail.find('.vas138-dsummary');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-act="open"]', function () { var id = currentDetailId; closeDetail(); zoomToCustomer(id); });
            $detail.on('click', '[data-detail-act="projects"]', function () { openProjects(currentDetailId, currentDetailName); });
        }

        /* ---------- Projects modal (reuses VAS_135 project endpoints) ---------- */

        function formatDate(iso) {
            if (!iso) { return '—'; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return iso; }
            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }
        function projStateLabel(state) {
            if (state === 'delayed') { return label('VAS_138_StateDelayed', 'Delayed'); }
            if (state === 'ontime') { return label('VAS_138_StateOnTime', 'On time'); }
            if (state === 'ongoing') { return label('VAS_138_StateOngoing', 'Ongoing'); }
            if (state === 'upcoming') { return label('VAS_138_StateUpcoming', 'Upcoming'); }
            return label('VAS_138_StateCompleted', 'Completed');
        }
        function projStatusClass(status) {
            if (status === 'Delayed') { return 'vas138-tag-delayed'; }
            if (status === 'Ongoing') { return 'vas138-tag-ongoing'; }
            if (status === 'Upcoming') { return 'vas138-tag-upcoming'; }
            return 'vas138-tag-ontime';
        }

        function openProjects(bpId, name) {
            if (!bpId) { return; }
            currentProjId = bpId;
            activeProjState = null;
            $projCust.text(name || currentDetailName || '');
            $proj.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas138-modal-open');
            $projBody.html('<div class="vas138-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetProjects',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $projBody.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_UnableToLoad', 'Unable to load delayed payments.')) + '</div>'); return; }
                    renderProjects(data || {});
                },
                error: function () { $projBody.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_UnableToLoad', 'Unable to load delayed payments.')) + '</div>'); }
            });
        }
        function closeProjects() {
            if (!$proj) { return; }
            if (document.activeElement && $proj[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $proj.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas138-modal-open'); }
        }
        function projStatCellHtml(state, count) {
            return '<button type="button" class="vas138-astat vas138-astat-' + state + (activeProjState === state ? ' is-active' : '') + '" data-state="' + state + '">' +
                '<b>' + formatCount(count) + '</b><span>' + escapeHtml(projStateLabel(state)) + '</span></button>';
        }
        function projectRowHtml(project) {
            var total = Number(project.total || 0);
            var progress = Number(project.progressPercent || 0);
            var breakdown = PROJECT_STATES.map(function (s) {
                var n = Number(project[s] || 0);
                return n > 0 ? '<span class="vas138-bd vas138-bd-' + s + '">' + formatCount(n) + ' ' + escapeHtml(projStateLabel(s)) + '</span>' : '';
            }).filter(function (x) { return x; }).join('');
            return '<div class="vas138-proj">' +
                '<div class="vas138-proj-head">' +
                    '<span class="vas138-proj-name" title="' + escapeHtml(project.name) + '">' + escapeHtml(project.name) + '</span>' +
                    '<span class="vas138-tag ' + projStatusClass(project.status) + '">' + escapeHtml(project.status) + '</span>' +
                '</div>' +
                (breakdown ? '<div class="vas138-proj-bd">' + breakdown + '</div>' : '') +
                '<div class="vas138-bar"><div style="width:' + Math.max(0, Math.min(100, progress)) + '%"></div></div>' +
                '<div class="vas138-proj-foot">' +
                    '<span>' + formatCount(project.completed) + '/' + formatCount(total) + ' ' + escapeHtml(label('VAS_138_Activities', 'activities')) + ' · ' + progress + '%</span>' +
                    '<span>' + escapeHtml(label('VAS_138_Due', 'Due')) + ' ' + escapeHtml(formatDate(project.dueDate)) + '</span>' +
                '</div>' +
            '</div>';
        }
        function renderProjects(data) {
            var summary = data.summary || {};
            var projects = data.projects || [];
            var cells = PROJECT_STATES.map(function (s) { return projStatCellHtml(s, summary[s] || 0); }).join('');
            var projRows = projects.length
                ? projects.map(projectRowHtml).join('')
                : '<div class="vas138-state">' + escapeHtml(label('VAS_138_NoProjects', 'No active projects.')) + '</div>';
            $projBody.html(
                '<div class="vas138-statgrid">' + cells + '</div>' +
                '<div class="vas138-actdetail"></div>' +
                (projects.length ? '<div class="vas138-projtitle">' + escapeHtml(label('VAS_138_Projects', 'Projects')) + ' (' + formatCount(projects.length) + ')</div>' : '') +
                '<div class="vas138-projects">' + projRows + '</div>'
            );
        }
        function toggleProjState(state) {
            var $detailBox = $proj.find('.vas138-actdetail');
            if (activeProjState === state) {
                activeProjState = null;
                $proj.find('.vas138-astat').removeClass('is-active');
                $detailBox.empty();
                return;
            }
            activeProjState = state;
            $proj.find('.vas138-astat').removeClass('is-active').filter('[data-state="' + state + '"]').addClass('is-active');
            $detailBox.html('<div class="vas138-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + PROJECT_ENDPOINT + 'GetActivityDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: currentProjId, state: state },
                success: function (response) {
                    if (activeProjState !== state) { return; }
                    var data = parseResponse(response);
                    var items = (data && data.items) || [];
                    if (!items.length) { $detailBox.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_NoActivities', 'No activities in this state.')) + '</div>'); return; }
                    var rows = items.map(function (a) {
                        return '<div class="vas138-act">' +
                            '<div class="vas138-act-main"><span class="vas138-act-name" title="' + escapeHtml(a.phaseName) + '">' + escapeHtml(a.phaseName) + '</span>' +
                                '<span class="vas138-act-proj" title="' + escapeHtml(a.projectName) + '">' + escapeHtml(a.projectName) + '</span></div>' +
                            '<span class="vas138-act-due">' + escapeHtml(label('VAS_138_Due', 'Due')) + ' ' + escapeHtml(formatDate(a.dueDate)) + '</span>' +
                        '</div>';
                    }).join('');
                    $detailBox.html('<div class="vas138-actlist">' + rows + '</div>');
                },
                error: function () { if (activeProjState === state) { $detailBox.html('<div class="vas138-state">' + escapeHtml(label('VAS_138_UnableToLoad', 'Unable to load delayed payments.')) + '</div>'); } }
            });
        }
        function createProjectsDialog() {
            $proj = $(
                '<div class="vas138-proj-modal" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_138_ActiveProjects', 'Active projects')) + '">' +
                    '<div class="vas138-scrim" data-proj-close></div>' +
                    '<section class="vas138-ppanel">' +
                        '<header class="vas138-phead"><h2 class="vas138-ptitle">' + escapeHtml(label('VAS_138_ActiveProjects', 'Active projects')) + '</h2>' +
                            '<div class="vas138-phead-right"><span class="vas138-psummary"></span>' +
                                '<button type="button" class="vas138-close" data-proj-close aria-label="' + escapeHtml(label('VAS_138_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas138-projbody"></div>' +
                        '<footer class="vas138-dfoot">' +
                            '<button type="button" class="vas138-btn vas138-btn-ghost" data-proj-close>' + escapeHtml(label('VAS_138_Customer', 'Customer')) + '</button>' +
                            '<button type="button" class="vas138-btn vas138-btn-primary" data-proj-act="open">' + icon('arrow') + escapeHtml(label('VAS_138_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($proj);
            $projBody = $proj.find('.vas138-projbody');
            $projCust = $proj.find('.vas138-psummary');
            $proj.on('click', '[data-proj-close]', closeProjects);
            $proj.on('click', '[data-proj-act="open"]', function () { var id = currentProjId; closeProjects(); zoomToCustomer(id); });
            $proj.on('click', '.vas138-astat', function () { toggleProjState($(this).attr('data-state')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas138-card">' +
                    '<div class="vas138-head">' +
                        '<div class="vas138-head-l">' +
                            '<span class="vas138-iconwell">' + icon('cash') + '</span>' +
                            '<div class="vas138-head-txt"><div class="vas138-title">' + escapeHtml(label('VAS_138_DelayedPayments', 'Delayed payments')) + '</div>' +
                                '<div class="vas138-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas138-alllink">' + escapeHtml(label('VAS_138_All', 'All')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas138-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas138-sub');
            $body = $card.find('.vas138-body');

            $card.on('click', '.vas138-alllink', function () { openAll(); });
            $card.on('click', '.vas138-open', function (e) { e.stopPropagation(); openCustomer(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas138-row', function () { openCustomer(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas138-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $card.on('click', '.vas138-retry', function () { loadSummary(); loadRows(); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createAllDialog();
            createDetailDialog();
            createProjectsDialog();

            $(document).on('keydown.MPCvas138', function (event) {
                if (event.key !== 'Escape') { return; }
                else if ($proj && $proj.hasClass('is-open')) { closeProjects(); }
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
            $(document).off('keydown.MPCvas138');
            if ($all) { $all.remove(); $all = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($proj) { $proj.remove(); $proj = null; }
            $('body').removeClass('vas138-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_138_DelayedPaymentsListWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_138_DelayedPaymentsListWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };
    VAS.VAS_138_DelayedPaymentsListWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_138_DelayedPaymentsListWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_138_DelayedPaymentsListWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_138_DelayedPaymentsListWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
