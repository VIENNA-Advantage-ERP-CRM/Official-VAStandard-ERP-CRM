/**
 * VAS_137 Needs Attention Widget (Customers module dashboard)
 * Purpose - 3x1 entry tiles (2x2 grid): four distinct-customer exception counts -
 *           Delayed payments (overdue), Unresponded, Not segmented, Onboarding open.
 *           Each tile is a keyboard-accessible button that opens that group's
 *           filtered, paged customer list; a row opens the customer record.
 * Design  - needs-attention.html (attached) + Design Specs/dashboard-widgets.md.
 *           Glass surface, icon-well header, 2x2 colour-coded tiles (icon, count,
 *           label, "Open list ->"). Internal sizing in em; borders/strokes in px.
 *           CSS namespaced vas137-* (Prompt_Instructions MPC prefix rule).
 *
 * Backend - VAS_137_NeedsAttentionWidget/GetSummary
 *           VAS_137_NeedsAttentionWidget/GetList  (flag=overdue|unresponded|unsegmented|onbIncomplete)
 *
 * Routing - 2026-08-17: hosted on a window, opening the customer navigates the host
 *           grid in place (widgetFirevalueChanged); on the Home / landing dashboard
 *           (windowNo < 0) there is no host grid, so the record is opened in the
 *           standard Customer window via VAS.ZoomUtil.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+------------------------------------------+--------------------------------
 *  1  | Needs attention                          | VAS_137_NeedsAttention
 *  2  | Delayed payments                         | VAS_137_DelayedPayments
 *  3  | Unresponded                              | VAS_137_Unresponded
 *  4  | Not segmented                            | VAS_137_NotSegmented
 *  5  | Onboarding open                          | VAS_137_OnboardingOpen
 *  6  | Open list                                | VAS_137_OpenList
 *  7  | invoices                                 | VAS_137_Invoices
 *  8  | invoice                                  | VAS_137_Invoice
 *  9  | overdue                                  | VAS_137_Overdue
 * 10  | open                                     | VAS_137_Open
 * 11  | waiting                                  | VAS_137_Waiting
 * 12  | No owner                                 | VAS_137_NoOwner
 * 13  | complete                                 | VAS_137_CompletePct
 * 14  | Nothing here right now.                  | VAS_137_NothingHere
 * 15  | No customers match this attention category. | VAS_137_NoMatch
 * 16  | Unable to load                           | VAS_137_UnableToLoad
 * 17  | Retry                                    | VAS_137_Retry
 * 18  | of                                       | VAS_137_Of
 * 19  | Close                                    | VAS_137_Close
 * 20  | Showing                                  | VAS_137_Showing
 * 21  | Previous page                            | VAS_137_PrevPage
 * 22  | Next page                                | VAS_137_NextPage
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var CUSTOMER_WINDOW_NAME = 'Business Partner';
    // Generic customer detail / activity-log endpoints (built with VAS_126).
    var CUSTOMER_ENDPOINT = 'VAS_126_OpenTicketsWidget/';

    /* 2026-08-17: zoom target when the widget is NOT hosted inside a window
       (windowNo < 0 - the Home / landing dashboard). There is no host grid to navigate
       there, so the record is opened in the standard Customer window; VAS.ZoomUtil
       resolves the AD_Window_ID from the new name, then the old name, then
       VAS_ZoomScreenConfig. */
    var ZOOM_WINDOW_NAME_NEW = 'VAS_CustomerMaster';
    var ZOOM_WINDOW_NAME_OLD = CUSTOMER_WINDOW_NAME;

    // Tile definitions in the required order.
    var TILES = [
        { flag: 'overdue', icon: 'cash', tone: 'danger', labelKey: 'VAS_137_DelayedPayments', labelText: 'Delayed payments' },
        { flag: 'unresponded', icon: 'mail', tone: 'warn', labelKey: 'VAS_137_Unresponded', labelText: 'Unresponded' },
        { flag: 'unsegmented', icon: 'layers', tone: 'violet', labelKey: 'VAS_137_NotSegmented', labelText: 'Not segmented' },
        { flag: 'onbIncomplete', icon: 'rocket', tone: 'info', labelKey: 'VAS_137_OnboardingOpen', labelText: 'Onboarding open' }
    ];

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

    VAS.VAS_137_NeedsAttentionWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas137-root">');
        var $grid;

        var $list, $listBody, $listPager, $listTitle;
        var listFlag = 'overdue', listOffset = 0, listTotal = 0, listSeq = 0;
        var listCurrency = { symbol: '', iso: '', precision: 0 };
        var LIST_PAGE = 7;

        // Customer detail modal state (reuses VAS_126 generic endpoints).
        var $detail, $detailBody, $detailSummary, currentDetailId = 0, currentDetailName = '';
        var detailCurrency = { symbol: '', iso: '', precision: 0 };

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
        // Compact money against a base (accounting-schema) currency descriptor.
        // VIS.Util.formatCompactAmount supplies the scaled magnitude at the
        // system-configured precision; the sign is composed before the symbol here.
        function formatMoney(value, cur) {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '';
            var iso = (cur && cur.iso) || '';
            var symbol = (cur && (cur.symbol || cur.iso)) || '';
            return sign + symbol + VIS.Util.formatCompactAmount(n, iso, precisionOf(cur));
        }
        function formatAmount(value) {
            return formatMoney(value, listCurrency);
        }

        function icon(name) {
            if (name === 'cash') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"></rect><circle cx="12" cy="12" r="2"></circle><path d="M6 12h.01M18 12h.01"></path></svg>'; }
            if (name === 'mail') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="4" width="20" height="16" rx="2"></rect><path d="m22 7-10 5L2 7"></path></svg>'; }
            if (name === 'layers') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>'; }
            if (name === 'rocket') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.71-.84.7-2.13-.09-2.91a2.18 2.18 0 0 0-2.91-.09z"></path><path d="m12 15-3-3a22 22 0 0 1 2-3.95A12.88 12.88 0 0 1 22 2c0 2.72-.78 7.5-6 11a22.35 22.35 0 0 1-4 2z"></path><path d="M9 12H4s.55-3.03 2-4c1.62-1.08 5 0 5 0"></path></svg>'; }
            if (name === 'arrow') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>'; }
            if (name === 'ticket') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v1a2 2 0 0 0 0 4v1a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-1a2 2 0 0 0 0-4z"></path><line x1="9" y1="7" x2="9" y2="17"></line></svg>'; }
            if (name === 'phone') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"></path></svg>'; }
            if (name === 'check') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>'; }
            if (name === 'close') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>'; }
            if (name === 'chev') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>'; }
            if (name === 'chevL') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>'; }
            return '';
        }

        /* ---------- Tiles ---------- */

        function tileHtml(tile) {
            return '<button type="button" class="vas137-tile MPC-tone-' + tile.tone + '" data-flag="' + tile.flag + '" tabindex="0">' +
                '<span class="vas137-tile-ic">' + icon(tile.icon) + '</span>' +
                '<span class="vas137-tile-num" data-num="' + tile.flag + '">…</span>' +
                '<span class="vas137-tile-lab">' + escapeHtml(label(tile.labelKey, tile.labelText)) + '</span>' +
                '<span class="vas137-tile-cta">' + escapeHtml(label('VAS_137_OpenList', 'Open list')) + ' ' + icon('arrow') + '</span>' +
            '</button>';
        }

        function loadSummary() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_137_NeedsAttentionWidget/GetSummary',
                type: 'GET', cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { showError(); return; }
                    TILES.forEach(function (tile) {
                        $grid.find('[data-num="' + tile.flag + '"]').text(formatCount(data[tile.flag] || 0));
                    });
                },
                error: showError
            });
        }

        function showError() {
            $grid.html('<div class="vas137-state">' + escapeHtml(label('VAS_137_UnableToLoad', 'Unable to load')) +
                ' <button type="button" class="vas137-retry">' + escapeHtml(label('VAS_137_Retry', 'Retry')) + '</button></div>');
        }

        /* ---------- Drill-down list modal ---------- */

        function tileMeta(flag) {
            for (var i = 0; i < TILES.length; i++) { if (TILES[i].flag === flag) { return TILES[i]; } }
            return TILES[0];
        }

        function openList(flag) {
            listFlag = flag;
            listOffset = 0;
            var meta = tileMeta(flag);
            $listTitle.text(label(meta.labelKey, meta.labelText));
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas137-modal-open');
            loadList();
        }
        function closeList() {
            if (!$list) { return; }
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas137-modal-open'); }
        }
        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas137-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_137_NeedsAttentionWidget/GetList',
                type: 'GET', cache: false,
                data: { flag: listFlag, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $listBody.html('<div class="vas137-state">' + escapeHtml(label('VAS_137_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    listTotal = Number(data.total || 0);
                    listCurrency.symbol = data.currency_symbol || '';
                    listCurrency.iso = data.currency_iso || '';
                    listCurrency.precision = data.std_precision;
                    renderList(data.items || []);
                },
                error: function () { if (seq === listSeq) { $listBody.html('<div class="vas137-state">' + escapeHtml(label('VAS_137_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }

        function rowMeta(item) {
            if (listFlag === 'overdue') {
                var invWord = Number(item.invoiceCount) === 1 ? label('VAS_137_Invoice', 'invoice') : label('VAS_137_Invoices', 'invoices');
                return formatCount(item.invoiceCount) + ' ' + invWord + ' · ' + formatCount(item.maxDays) + 'd ' + label('VAS_137_Overdue', 'overdue');
            }
            if (listFlag === 'unresponded') {
                return formatCount(item.requestCount) + ' ' + label('VAS_137_Open', 'open') + ' · ' + formatCount(item.maxWaitDays) + 'd ' + label('VAS_137_Waiting', 'waiting');
            }
            if (listFlag === 'unsegmented') {
                return [item.code, item.rep || label('VAS_137_NoOwner', 'No owner')].filter(function (p) { return p; }).join(' · ');
            }
            return (item.onbStep || '');
        }
        function rowValue(item) {
            if (listFlag === 'overdue') { return '<span class="vas137-row-val vas137-danger">' + escapeHtml(formatAmount(item.amount)) + '</span>'; }
            if (listFlag === 'onbIncomplete') { return '<span class="vas137-row-val">' + Number(item.progress || 0) + '%</span>'; }
            return '';
        }

        function renderList(items) {
            if (!items.length) {
                $listBody.html('<div class="vas137-state">' + escapeHtml(label('VAS_137_NoMatch', 'No customers match this attention category.')) + '</div>');
                $listPager.empty();
                return;
            }
            var rows = items.map(function (item) {
                var meta = rowMeta(item);
                return '<button type="button" class="vas137-row" data-id="' + Number(item.id) + '">' +
                    '<span class="vas137-row-main">' +
                        '<span class="vas137-row-title" title="' + escapeHtml(item.name) + '">' + escapeHtml(item.name) + '</span>' +
                        '<span class="vas137-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                    '</span>' +
                    rowValue(item) +
                '</button>';
            }).join('');

            var start = listOffset + 1;
            var end = listOffset + items.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            /* Footer pager (dashboard-widgets.md §"Widget Footer Pager"): helper
               left, compact prev · "N of M" · next right. The control stays put
               on a single page with both arrows disabled. */
            var of = label('VAS_137_Of', 'of');
            var helper = label('VAS_137_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listTotal);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;

            $listBody.html('<div class="vas137-list">' + rows + '</div>');
            $listPager.html(
                '<span class="vas137-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas137-pgctl">' +
                    '<button type="button" class="vas137-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_137_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas137-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas137-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_137_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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

        function createListDialog() {
            $list = $(
                '<div class="vas137-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas137-scrim" data-list-close></div>' +
                    '<section class="vas137-panel">' +
                        '<header class="vas137-phead"><h2 class="vas137-ptitle"></h2>' +
                            '<button type="button" class="vas137-close" data-list-close aria-label="' + escapeHtml(label('VAS_137_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas137-pbody"></div>' +
                        '<footer class="vas137-pfoot"><div class="vas137-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas137-pbody');
            $listPager = $list.find('.vas137-pager');
            $listTitle = $list.find('.vas137-ptitle');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '.vas137-row', function () { openCustomer(Number($(this).attr('data-id'))); });
            $list.on('click', '.vas137-pgbtn', function () { turnPage($(this).attr('data-dir')); });
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

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
            var cls = 'vas137-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas137-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas137-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas137-tag-info'; }
            return '<span class="vas137-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }

        function anyModalOpen() {
            return ($list && $list.hasClass('is-open')) || ($detail && $detail.hasClass('is-open'));
        }

        // Currency formatter for the detail modal (independent of the list currency).
        function formatDetailAmount(value) {
            return formatMoney(value, detailCurrency);
        }

        function openCustomer(bpId) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas137-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas137-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas137-state">' + escapeHtml(label('VAS_137_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas137-state">' + escapeHtml(label('VAS_137_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas137-fact"><div class="vas137-fl">' + escapeHtml(fallback) + '</div><div class="vas137-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas137-signal"><span class="vas137-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas137-sig-main"><div class="vas137-sig-name">' + titleHtml + '</div><div class="vas137-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
        }

        function renderCustomerDetail(data) {
            detailCurrency.symbol = data.currency_symbol || '';
            detailCurrency.iso = data.currency_iso || '';
            detailCurrency.precision = data.std_precision;
            var dash = '—';
            var name = data.name || '';
            currentDetailName = name;
            var sub = [data.contactName, data.contactEmail].filter(function (p) { return p; }).join(' · ');
            var tierLabel = data.tier || data.tierCode || '';
            var summaryParts = [];
            if (tierLabel) { summaryParts.push(tierLabel); }
            if (data.isKeyClient) { summaryParts.push(label('VAS_137_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas137-factgrid">' +
                fact(label('VAS_137_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_137_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_137_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_137_ARR', 'ARR'), escapeHtml(formatDetailAmount(data.value))) +
                fact(label('VAS_137_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_137_Projects', 'Projects'), projectText(data.projectName, projects, dash)) +
                fact(label('VAS_137_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatDetailAmount(pipeline)) : dash) +
                fact(label('VAS_137_Onboarding', 'Onboarding'), data.onboardingPercent == null ? dash : escapeHtml(formatCount(data.onboardingPercent) + '%')) +
            '</div>';

            var signals = '';
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_137_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_137_KeyPrioritise', 'Key client — prioritise') : label('VAS_137_OpenRequests', 'Open support requests'));
            }
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatDetailAmount(overdue) + ' ' + label('VAS_137_Overdue', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_137_DaysPastDue', 'days past due'));
            }
            var signalsBlock = signals ? '<div class="vas137-signals"><div class="vas137-sig-title">' + escapeHtml(label('VAS_137_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas137-id">' +
                '<span class="vas137-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas137-id-main"><div class="vas137-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas137-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas137-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas137-modal-open'); }
        }

        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            closeDetail();
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

        function createDetailDialog() {
            $detail = $(
                '<div class="vas137-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_137_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas137-scrim" data-detail-close></div>' +
                    '<section class="vas137-dpanel">' +
                        '<header class="vas137-phead"><h2 class="vas137-ptitle">' + escapeHtml(label('VAS_137_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas137-phead-right"><span class="vas137-dsummary"></span>' +
                                '<button type="button" class="vas137-close" data-detail-close aria-label="' + escapeHtml(label('VAS_137_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas137-dbody"></div>' +
                        '<footer class="vas137-dfoot">' +
                            '<button type="button" class="vas137-btn vas137-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_137_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas137-dbody');
            $detailSummary = $detail.find('.vas137-dsummary');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-act="open"]', function () { var id = currentDetailId; zoomToCustomer(id); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas137-card">' +
                    '<div class="vas137-head">' +
                        '<span class="vas137-iconwell">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m21.73 18-8-14a2 2 0 0 0-3.46 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>' +
                        '</span>' +
                        '<div class="vas137-head-txt">' +
                            '<div class="vas137-title">' + escapeHtml(label('VAS_137_NeedsAttention', 'Needs attention')) + '</div>' +
                            '<div class="vas137-sub">' + escapeHtml(label('VAS_137_FilteredLists', 'Filtered customer lists')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas137-grid">' + TILES.map(tileHtml).join('') + '</div>' +
                '</div>'
            );
            $grid = $card.find('.vas137-grid');
            $card.on('click', '.vas137-tile', function () { openList($(this).attr('data-flag')); });
            $card.on('keydown', '.vas137-tile', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); openList($(this).attr('data-flag')); }
            });
            $card.on('click', '.vas137-retry', function () { $grid.html(TILES.map(tileHtml).join('')); loadSummary(); });
            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createListDialog();
            createDetailDialog();
            $(document).on('keydown.MPCvas137', function (event) {
                if (event.key !== 'Escape') { return; }
                else if ($detail && $detail.hasClass('is-open')) { closeDetail(); }
                else if ($list && $list.hasClass('is-open')) { closeList(); }
            });
            loadSummary();
        };

        this.refreshWidget = function () { loadSummary(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas137');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            $('body').removeClass('vas137-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_137_NeedsAttentionWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_137_NeedsAttentionWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_137_NeedsAttentionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_137_NeedsAttentionWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_137_NeedsAttentionWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_137_NeedsAttentionWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
