/**
 * VAS_301 Sales Rep Portfolio Widget (Customers module dashboard)
 * Purpose - 3x2 glass leaderboard: sales reps ranked by owned ARR, each row
 *           showing an ARR bar (relative to the top rep), account count, open
 *           account count (an account with at least one open opportunity) and
 *           an at-risk tag - amber "{n} at risk" when the rep has any account
 *           with an overdue receivable or an open support ticket, else green
 *           "Healthy". Header shows "{n} reps · {ownedARR} owned ARR". Row
 *           click opens that rep's customers, ranked by ARR; a row zooms to
 *           the customer record.
 * Design  - sales-rep-portfolio.html (attached) + Design Specs/
 *           dashboard-widgets.md. Glass surface, icon-well header, avatar +
 *           bar leaderboard rows, drill-down list modal. Internal sizing in
 *           em against the widget-root clamp; borders/strokes in px. CSS
 *           namespaced vas301-* (MPC prefix rule). Row/bar/amt structure
 *           matches VAS_139's pipeline leaderboard; the drill modal's simple
 *           zoom-on-click matches VAS_141/300.
 *
 * Backend - VAS_301_SalesRepPortfolioWidget/GetLeaderboard (paged ranked rows + totals)
 *           VAS_301_SalesRepPortfolioWidget/GetRepList     (one rep's customers)
 *
 * Routing - hosted on a window, opening the customer navigates the host grid
 *           in place (widgetFirevalueChanged); on the Home / landing
 *           dashboard (windowNo < 0) there is no host grid, so the record is
 *           opened in the standard Customer window via VAS.ZoomUtil.
 *           Matches VAS_135/138/141/295-300.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+----------------------------------+--------------------------------
 *  1  | Sales rep portfolio             | VAS_301_SalesRepPortfolio
 *  2  | reps                            | VAS_301_Reps
 *  3  | rep                             | VAS_301_Rep
 *  4  | owned ARR                       | VAS_301_OwnedARR
 *  5  | accts                           | VAS_301_Accts
 *  6  | opps                            | VAS_301_Opps
 *  7  | at risk                         | VAS_301_AtRisk
 *  8  | Healthy                         | VAS_301_Healthy
 *  9  | · portfolio                     | VAS_301_PortfolioSuffix
 * 10  | Nothing here right now.         | VAS_301_NothingHere
 * 11  | No customers.                   | VAS_301_NoCustomers
 * 12  | Unable to load                  | VAS_301_UnableToLoad
 * 13  | Retry                           | VAS_301_Retry
 * 14  | of                              | VAS_301_Of
 * 15  | Showing                         | VAS_301_Showing
 * 16  | Previous page                   | VAS_301_PrevPage
 * 17  | Next page                       | VAS_301_NextPage
 * 18  | Close                           | VAS_301_Close
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    VAS.VAS_301_SalesRepPortfolioWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas301-root">');
        var $sub, $body;

        var pageSize = 7;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;
        var widgetCurrency = { symbol: '', iso: '', precision: 2 };

        // Rep drill-down modal state.
        var $list, $listBody, $listPager, $listTitle;
        var listRepId = 0, listOffset = 0, listRepTotal = 0, listSeq = 0;
        var listCurrency = { symbol: '', iso: '', precision: 2 };
        var LIST_PAGE = 7;

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
        var AVATAR_COLORS = ['#1F83FF', '#5F4AA6', '#0B6B45', '#D78B10', '#0083DA', '#A33F3F'];
        function avatarColor(text) {
            var hash = 0, value = String(text || '');
            for (var i = 0; i < value.length; i++) { hash = (hash * 31 + value.charCodeAt(i)) % AVATAR_COLORS.length; }
            return AVATAR_COLORS[hash];
        }
        function initials(name) {
            return String(name || '').split(' ').slice(0, 2).map(function (w) { return w.charAt(0); }).join('').toUpperCase();
        }

        function icon(name) {
            if (name === 'users') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"></path><circle cx="9" cy="7" r="4"></circle><path d="M22 21v-2a4 4 0 0 0-3-3.87"></path><path d="M16 3.13a4 4 0 0 1 0 7.75"></path></svg>';
            }
            if (name === 'chev') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>';
            }
            if (name === 'chevL') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>';
            }
            if (name === 'close') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>';
            }
            return '';
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_301_SalesRepPortfolioWidget/GetLeaderboard',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    listTotal = Number(data.total || 0);
                    widgetCurrency = { symbol: data.currency_symbol || widgetCurrency.symbol, iso: data.currency_iso || widgetCurrency.iso, precision: data.std_precision };
                    var repWord = listTotal === 1 ? label('VAS_301_Rep', 'rep') : label('VAS_301_Reps', 'reps');
                    $sub.text(formatCount(listTotal) + ' ' + repWord + ' · ' + formatMoney(data.totalArr, widgetCurrency) + ' ' + label('VAS_301_OwnedARR', 'owned ARR'));
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) { $body.html('<div class="vas301-state">' + escapeHtml(message) + '</div>'); }
        function renderErrorState() {
            $body.html('<div class="vas301-state">' + escapeHtml(label('VAS_301_UnableToLoad', 'Unable to load')) +
                ' <button type="button" class="vas301-retry">' + escapeHtml(label('VAS_301_Retry', 'Retry')) + '</button></div>');
        }

        function rowHtml(item, cur) {
            var name = item.repName || '';
            var bar = Math.max(0, Math.min(100, Number(item.barPercent || 0)));
            var riskTag = Number(item.riskCount || 0) > 0
                ? '<span class="vas301-tag vas301-tag-warn">' + formatCount(item.riskCount) + ' ' + escapeHtml(label('VAS_301_AtRisk', 'at risk')) + '</span>'
                : '<span class="vas301-tag vas301-tag-ontime">' + escapeHtml(label('VAS_301_Healthy', 'Healthy')) + '</span>';
            var meta = formatCount(item.count) + ' ' + label('VAS_301_Accts', 'accts') + ' · ' + formatCount(item.openCount) + ' ' + label('VAS_301_Opps', 'opps');
            return '<div class="vas301-row" data-id="' + Number(item.repId) + '" data-name="' + escapeHtml(name) + '">' +
                '<span class="vas301-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<span class="vas301-row-main">' +
                    '<span class="vas301-row-title" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                    '<span class="vas301-bar"><div style="width:' + bar + '%"></div></span>' +
                '</span>' +
                '<span class="vas301-row-amt">' +
                    '<span class="vas301-amt-val">' + escapeHtml(formatMoney(item.arr, cur)) + '</span>' +
                    '<span class="vas301-amt-lab">' + meta + '</span>' +
                '</span>' +
                riskTag +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) { renderState(label('VAS_301_NothingHere', 'Nothing here right now.')); return; }
            var cur = widgetCurrency;
            var rows = items.map(function (it) { return rowHtml(it, cur); }).join('');
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas301-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + rows + '</div>' + pagerHtml(pageOffset, pageSize, listTotal));
        }

        /* Footer pager (Design Specs/dashboard-widgets.md §"Widget Footer Pager"):
           "Showing X–Y of Z" helper on the left, compact prev · "N of M" · next
           control on the right. */
        function pagerHtml(offset, size, total) {
            var start = offset + 1;
            var pages = Math.max(1, Math.ceil(total / size));
            var current = Math.floor(offset / size);
            var end = Math.min(offset + size, total);
            var of = label('VAS_301_Of', 'of');
            var helper = label('VAS_301_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(total);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;
            return '<div class="vas301-pager">' +
                '<span class="vas301-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas301-pgctl">' +
                    '<button type="button" class="vas301-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_301_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas301-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas301-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_301_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>' +
            '</div>';
        }

        function turnPage(direction) {
            var next = pageOffset + (direction === 'next' ? pageSize : -pageSize);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            pageOffset = next;
            loadRows();
        }

        /* ---------- Rep drill-down modal ---------- */

        function openRepList(repId, repName) {
            if (!repId) { return; }
            listRepId = repId;
            listOffset = 0;
            $listTitle.text((repName || '') + ' ' + label('VAS_301_PortfolioSuffix', '· portfolio'));
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas301-modal-open');
            loadList();
        }
        function closeList() {
            if (!$list) { return; }
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            $('body').removeClass('vas301-modal-open');
        }
        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas301-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_301_SalesRepPortfolioWidget/GetRepList',
                type: 'GET', cache: false,
                data: { repId: listRepId, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $listBody.html('<div class="vas301-state">' + escapeHtml(label('VAS_301_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    listRepTotal = Number(data.total || 0);
                    listCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
                    renderListRows(data.items || []);
                },
                error: function () { if (seq === listSeq) { $listBody.html('<div class="vas301-state">' + escapeHtml(label('VAS_301_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }
        function custRowHtml(item) {
            var name = item.customerName || '';
            return '<button type="button" class="vas301-row2" data-id="' + Number(item.customerId) + '">' +
                '<span class="vas301-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<span class="vas301-row-main">' +
                    '<span class="vas301-row-title" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                '</span>' +
                '<span class="vas301-row-val">' + escapeHtml(formatMoney(item.arr, listCurrency)) + '</span>' +
            '</button>';
        }
        function renderListRows(items) {
            if (!items.length) {
                $listBody.html('<div class="vas301-state">' + escapeHtml(label('VAS_301_NoCustomers', 'No customers.')) + '</div>');
                $listPager.empty();
                return;
            }
            $listBody.html('<div class="vas301-list2">' + items.map(custRowHtml).join('') + '</div>');
            var start = listOffset + 1, end = listOffset + items.length;
            var pages = Math.max(1, Math.ceil(listRepTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_301_Of', 'of');
            var helper = label('VAS_301_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listRepTotal);
            $listPager.html(
                '<span class="vas301-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas301-pgctl">' +
                    '<button type="button" class="vas301-pgbtn" data-listdir="prev" aria-label="' + escapeHtml(label('VAS_301_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas301-pgtext">' + escapeHtml((current + 1) + ' ' + of + ' ' + pages) + '</span>' +
                    '<button type="button" class="vas301-pgbtn" data-listdir="next" aria-label="' + escapeHtml(label('VAS_301_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>'
            );
        }
        function turnListPage(direction) {
            var next = listOffset + (direction === 'next' ? LIST_PAGE : -LIST_PAGE);
            if (next < 0) { next = 0; }
            if (next >= listRepTotal) { return; }
            listOffset = next;
            loadList();
        }

        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            closeList();
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

        function createListDialog() {
            $list = $(
                '<div class="vas301-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas301-scrim" data-list-close></div>' +
                    '<section class="vas301-panel">' +
                        '<header class="vas301-phead"><h2 class="vas301-ptitle"></h2>' +
                            '<button type="button" class="vas301-close" data-list-close aria-label="' + escapeHtml(label('VAS_301_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas301-pbody"></div>' +
                        '<footer class="vas301-pfoot"><div class="vas301-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas301-pbody');
            $listPager = $list.find('.vas301-pager');
            $listTitle = $list.find('.vas301-ptitle');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '.vas301-row2', function () { zoomToCustomer(Number($(this).attr('data-id'))); });
            $list.on('click', '.vas301-pgbtn', function () { turnListPage($(this).attr('data-listdir')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas301-card">' +
                    '<div class="vas301-head">' +
                        '<div class="vas301-head-l">' +
                            '<span class="vas301-iconwell">' + icon('users') + '</span>' +
                            '<div class="vas301-head-txt"><div class="vas301-title">' + escapeHtml(label('VAS_301_SalesRepPortfolio', 'Sales rep portfolio')) + '</div>' +
                                '<div class="vas301-sub"></div></div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas301-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas301-sub');
            $body = $card.find('.vas301-body');

            $card.on('click', '.vas301-row', function () { openRepList(Number($(this).attr('data-id')), $(this).attr('data-name')); });
            $card.on('click', '.vas301-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $card.on('click', '.vas301-retry', function () { loadRows(); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createListDialog();
            $(document).on('keydown.MPCvas301', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($list && $list.hasClass('is-open')) { closeList(); }
            });
            loadRows();
        };

        this.refreshWidget = function () { pageOffset = 0; loadRows(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas301');
            if ($list) { $list.remove(); $list = null; }
            $('body').removeClass('vas301-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_301_SalesRepPortfolioWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_301_SalesRepPortfolioWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_301_SalesRepPortfolioWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_301_SalesRepPortfolioWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_301_SalesRepPortfolioWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_301_SalesRepPortfolioWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
