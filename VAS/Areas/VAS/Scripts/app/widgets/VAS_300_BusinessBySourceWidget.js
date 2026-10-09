/**
 * VAS_300 Business by Source Widget (Customers module dashboard)
 * Purpose - 3x2 glass distribution: booked business (completed sales orders,
 *           same eligibility as VAS_273's "Order Value Booked") grouped by
 *           customer acquisition source (R_Source), over a 3- or 6-month
 *           trailing window picked by a header pill toggle. Each row is a
 *           source with a coloured swatch, its booked value + share of the
 *           period total, and a distribution bar. Clicking a source opens its
 *           customers (ranked by the active period's business); a row zooms
 *           to the customer record.
 * Design  - business-by-source.html (attached) + Design Specs/
 *           dashboard-widgets.md. Glass surface, icon-well header, a compact
 *           3mo/6mo pill toggle on the header's right, coloured distribution
 *           rows + bars, drill-down list modal. Internal sizing in em against
 *           the widget-root clamp; borders/strokes in px. CSS namespaced
 *           vas300-* (MPC prefix rule). Row/bar/pager structure matches
 *           VAS_141 (the codebase's other distribution widget) closely.
 *
 * Backend - VAS_300_BusinessBySourceWidget/GetDistribution (period=biz3|biz6)  (per-source totals)
 *           VAS_300_BusinessBySourceWidget/GetSourceList    (sourceId, period, offset, limit)
 *
 * Routing - hosted on a window, opening the customer navigates the host grid
 *           in place (widgetFirevalueChanged); on the Home / landing
 *           dashboard (windowNo < 0) there is no host grid, so the record is
 *           opened in the standard Customer window via VAS.ZoomUtil.
 *           Matches VAS_135/138/141/295-299.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+----------------------------------+--------------------------------
 *  1  | Business by source              | VAS_300_BusinessBySource
 *  2  | last                            | VAS_300_Last
 *  3  | months                          | VAS_300_Months
 *  4  | 3 mo                            | VAS_300_3Mo
 *  5  | 6 mo                            | VAS_300_6Mo
 *  6  | Source ·                        | VAS_300_SourceTitle (prefix)
 *  7  | No owner                        | VAS_300_NoOwner
 *  8  | Nothing here right now.         | VAS_300_NothingHere
 *  9  | No customers.                   | VAS_300_NoCustomers
 * 10  | Unable to load                  | VAS_300_UnableToLoad
 * 11  | Retry                           | VAS_300_Retry
 * 12  | of                              | VAS_300_Of
 * 13  | Showing                         | VAS_300_Showing
 * 14  | Previous page                   | VAS_300_PrevPage
 * 15  | Next page                       | VAS_300_NextPage
 * 16  | Close                           | VAS_300_Close
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

    VAS.VAS_300_BusinessBySourceWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas300-root">');
        var $sub, $body, $tog;

        // Module state: the active period. Matches the reference's SRC_PERIOD.
        var srcPeriod = 'biz3';

        var widgetCurrency = { symbol: '', iso: '', precision: 2 };

        // Distribution pagination (client-side over the returned sources - a
        // small, tenant-configured set, same technique as VAS_141's segments).
        var allSources = [], srcPage = 0, srcPageSize = 5, distTotal = 0;

        // Source drill-down modal state.
        var $list, $listBody, $listPager, $listTitle;
        var listSourceId = 0, listOffset = 0, listTotal = 0, listSeq = 0;
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
            if (name === 'trend') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="22 7 13.5 15.5 8.5 10.5 2 17"></polyline><polyline points="16 7 22 7 22 13"></polyline></svg>';
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

        /* ---------- Period toggle ---------- */

        function togHtml() {
            var is3 = srcPeriod === 'biz3';
            return '<div class="vas300-tog">' +
                '<button type="button" class="vas300-togbtn' + (is3 ? ' is-active' : '') + '" data-period="biz3">' + escapeHtml(label('VAS_300_3Mo', '3 mo')) + '</button>' +
                '<button type="button" class="vas300-togbtn' + (!is3 ? ' is-active' : '') + '" data-period="biz6">' + escapeHtml(label('VAS_300_6Mo', '6 mo')) + '</button>' +
            '</div>';
        }
        function setSrcPeriod(period) {
            if (period !== 'biz3' && period !== 'biz6') { return; }
            if (period === srcPeriod) { return; }
            srcPeriod = period;
            $tog.html(togHtml());
            loadDistribution();
        }

        /* ---------- Distribution ---------- */

        function loadDistribution() {
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_300_BusinessBySourceWidget/GetDistribution',
                type: 'GET', cache: false,
                data: { period: srcPeriod },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { renderError(); return; }
                    widgetCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
                    var months = srcPeriod === 'biz6' ? '6' : '3';
                    $sub.text(formatMoney(data.total, widgetCurrency) + ' · ' + label('VAS_300_Last', 'last') + ' ' + months + ' ' + label('VAS_300_Months', 'months'));
                    renderSources(data.items || [], Number(data.total || 0));
                },
                error: renderError
            });
        }

        function renderState(message) { $body.html('<div class="vas300-state">' + escapeHtml(message) + '</div>'); }
        function renderError() {
            $body.html('<div class="vas300-state">' + escapeHtml(label('VAS_300_UnableToLoad', 'Unable to load')) +
                ' <button type="button" class="vas300-retry">' + escapeHtml(label('VAS_300_Retry', 'Retry')) + '</button></div>');
        }

        function srcRowHtml(src, total) {
            var share = total > 0 ? Math.max(0, Math.min(100, Math.round(src.value / total * 100))) : 0;
            return '<div class="vas300-src">' +
                '<div class="vas300-src-top">' +
                    '<button type="button" class="vas300-src-l" data-id="' + Number(src.sourceId) + '" data-name="' + escapeHtml(src.name) + '">' +
                        '<span class="vas300-sw" style="background:' + src.color + '"></span>' +
                        '<span class="vas300-src-name" title="' + escapeHtml(src.name) + '">' + escapeHtml(src.name) + '</span>' +
                    '</button>' +
                    '<span class="vas300-src-v">' + escapeHtml(formatMoney(src.value, widgetCurrency)) + ' · ' + share + '%</span>' +
                '</div>' +
                '<div class="vas300-bar"><div style="width:' + share + '%;background:' + src.color + '"></div></div>' +
            '</div>';
        }

        function renderSources(items, total) {
            allSources = items || [];
            distTotal = total || 0;
            srcPage = 0;
            if (!allSources.length) {
                renderState(label('VAS_300_NothingHere', 'Nothing here right now.'));
                return;
            }
            renderSrcPage();
        }

        // Renders the current page of sources (5/page) plus a circular pager when
        // there is more than one page. Matches VAS_141's segment paging exactly.
        function renderSrcPage() {
            var count = allSources.length;
            var pages = Math.max(1, Math.ceil(count / srcPageSize));
            if (srcPage > pages - 1) { srcPage = pages - 1; }
            if (srcPage < 0) { srcPage = 0; }
            var start = srcPage * srcPageSize;
            var slice = allSources.slice(start, start + srcPageSize);
            var rows = slice.map(function (s) { return srcRowHtml(s, distTotal); }).join('');
            var html = '<div class="vas300-srclist" style="grid-template-rows: repeat(' + srcPageSize + ', minmax(0, 1fr))">' + rows + '</div>';
            if (pages > 1) {
                var from = start + 1, to = start + slice.length;
                var srcOf = label('VAS_300_Of', 'of');
                var srcHelper = label('VAS_300_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + srcOf + ' ' + formatCount(count);
                html += '<div class="vas300-wpager">' +
                    '<span class="vas300-wpglabel">' + escapeHtml(srcHelper) + '</span>' +
                    '<span class="vas300-wpgctl">' +
                        '<button type="button" class="vas300-wpgbtn" data-srcdir="prev" aria-label="' + escapeHtml(label('VAS_300_PrevPage', 'Previous page')) + '" ' + (srcPage <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                        '<span class="vas300-wpgtext">' + escapeHtml((srcPage + 1) + ' ' + srcOf + ' ' + pages) + '</span>' +
                        '<button type="button" class="vas300-wpgbtn" data-srcdir="next" aria-label="' + escapeHtml(label('VAS_300_NextPage', 'Next page')) + '" ' + (srcPage >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                    '</span>' +
                '</div>';
            }
            $body.html(html);
        }

        function turnSrcPage(dir) {
            var pages = Math.max(1, Math.ceil(allSources.length / srcPageSize));
            if (dir === 'next' && srcPage < pages - 1) { srcPage++; }
            else if (dir === 'prev' && srcPage > 0) { srcPage--; }
            else { return; }
            renderSrcPage();
        }

        /* ---------- Source drill-down modal ---------- */

        function openSourceList(sourceId, sourceName) {
            if (!sourceId) { return; }
            listSourceId = sourceId;
            listOffset = 0;
            $listTitle.text(label('VAS_300_SourceTitle', 'Source') + ' · ' + (sourceName || ''));
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas300-modal-open');
            loadList();
        }
        function closeSourceList() {
            if (!$list) { return; }
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            $('body').removeClass('vas300-modal-open');
        }
        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas300-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_300_BusinessBySourceWidget/GetSourceList',
                type: 'GET', cache: false,
                data: { sourceId: listSourceId, period: srcPeriod, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $listBody.html('<div class="vas300-state">' + escapeHtml(label('VAS_300_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    listTotal = Number(data.total || 0);
                    listCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
                    renderListRows(data.items || []);
                },
                error: function () { if (seq === listSeq) { $listBody.html('<div class="vas300-state">' + escapeHtml(label('VAS_300_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }
        function custRowHtml(item) {
            var name = item.customerName || '';
            var meta = item.ownerName || label('VAS_300_NoOwner', 'No owner');
            return '<button type="button" class="vas300-row" data-id="' + Number(item.customerId) + '">' +
                '<span class="vas300-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<span class="vas300-row-main">' +
                    '<span class="vas300-row-title" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                    '<span class="vas300-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas300-row-val">' + escapeHtml(formatMoney(item.amount, listCurrency)) + '</span>' +
            '</button>';
        }
        function renderListRows(items) {
            if (!items.length) {
                $listBody.html('<div class="vas300-state">' + escapeHtml(label('VAS_300_NoCustomers', 'No customers.')) + '</div>');
                $listPager.empty();
                return;
            }
            $listBody.html('<div class="vas300-list">' + items.map(custRowHtml).join('') + '</div>');
            var start = listOffset + 1, end = listOffset + items.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_300_Of', 'of');
            var helper = label('VAS_300_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listTotal);
            $listPager.html(
                '<span class="vas300-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas300-pgctl">' +
                    '<button type="button" class="vas300-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_300_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas300-pgtext">' + escapeHtml((current + 1) + ' ' + of + ' ' + pages) + '</span>' +
                    '<button type="button" class="vas300-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_300_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>'
            );
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
            closeSourceList();
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
                '<div class="vas300-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas300-scrim" data-list-close></div>' +
                    '<section class="vas300-panel">' +
                        '<header class="vas300-phead"><h2 class="vas300-ptitle"></h2>' +
                            '<button type="button" class="vas300-close" data-list-close aria-label="' + escapeHtml(label('VAS_300_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas300-pbody"></div>' +
                        '<footer class="vas300-pfoot"><div class="vas300-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas300-pbody');
            $listPager = $list.find('.vas300-pager');
            $listTitle = $list.find('.vas300-ptitle');
            $list.on('click', '[data-list-close]', closeSourceList);
            $list.on('click', '.vas300-row', function () { zoomToCustomer(Number($(this).attr('data-id'))); });
            $list.on('click', '.vas300-pgbtn', function () { turnPage($(this).attr('data-dir')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas300-card">' +
                    '<div class="vas300-head">' +
                        '<div class="vas300-head-l">' +
                            '<span class="vas300-iconwell">' + icon('trend') + '</span>' +
                            '<div class="vas300-head-txt"><div class="vas300-title">' + escapeHtml(label('VAS_300_BusinessBySource', 'Business by source')) + '</div>' +
                                '<div class="vas300-sub"></div></div>' +
                        '</div>' +
                        '<div class="vas300-togwrap">' + togHtml() + '</div>' +
                    '</div>' +
                    '<div class="vas300-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas300-sub');
            $body = $card.find('.vas300-body');
            $tog = $card.find('.vas300-togwrap');

            $card.on('click', '.vas300-togbtn', function () { setSrcPeriod($(this).attr('data-period')); });
            $card.on('click', '.vas300-src-l', function () { openSourceList(Number($(this).attr('data-id')), $(this).attr('data-name')); });
            $card.on('click', '.vas300-wpgbtn', function () { turnSrcPage($(this).attr('data-srcdir')); });
            $card.on('click', '.vas300-retry', function () { loadDistribution(); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createListDialog();
            $(document).on('keydown.MPCvas300', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($list && $list.hasClass('is-open')) { closeSourceList(); }
            });
            loadDistribution();
        };

        this.refreshWidget = function () { loadDistribution(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas300');
            if ($list) { $list.remove(); $list = null; }
            $('body').removeClass('vas300-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_300_BusinessBySourceWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_300_BusinessBySourceWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_300_BusinessBySourceWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_300_BusinessBySourceWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_300_BusinessBySourceWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_300_BusinessBySourceWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
