/**
 * VAS_299 Revenue at a Glance Widget (Customers module dashboard)
 * Purpose - 3x2 glass tile: a 2x2 grid of the four money figures that matter -
 *           open pipeline (blue), open proposals (violet), contracts ending
 *           <=90 days (amber) and overdue A/R (red) - each converted to the
 *           tenant accounting currency so the four are comparable. Each tile
 *           is a keyboard-accessible button that opens that figure's ranked,
 *           paged customer list; a row opens the customer record.
 * Design  - revenue-at-a-glance.html (attached) + Design Specs/
 *           dashboard-widgets.md. Glass surface, icon-well header, 2x2
 *           colour-coded tiles (icon, value, sub-caption). Internal sizing in
 *           em; borders/strokes in px. CSS namespaced vas299-* (MPC prefix
 *           rule). Tile grid/tone pattern matches VAS_137 (the codebase's
 *           other 2x2 tile widget) exactly, adapted for money values instead
 *           of plain counts.
 *
 * Backend - VAS_299_RevenueAtAGlanceWidget/GetSummary                (all four totals)
 *           VAS_299_RevenueAtAGlanceWidget/GetList (flag=pipeline|proposals|contracts|overdue)
 *           Customer detail reuses the generic VAS_126 endpoint:
 *             VAS_126_OpenTicketsWidget/GetCustomerDetail
 *
 * Routing - hosted on a window, opening the customer navigates the host grid
 *           in place (widgetFirevalueChanged); on the Home / landing
 *           dashboard (windowNo < 0) there is no host grid, so the record is
 *           opened in the standard Customer window via VAS.ZoomUtil.
 *           Matches VAS_135/137/138/295-298.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+----------------------------------+--------------------------------
 *  1  | Revenue at a glance             | VAS_299_RevenueAtAGlance
 *  2  | Pipeline, proposals, contracts & A/R | VAS_299_HeaderSub
 *  3  | Open pipeline                   | VAS_299_OpenPipeline
 *  4  | Open proposals                  | VAS_299_OpenProposals
 *  5  | Contracts ≤90d                  | VAS_299_ContractsLE90
 *  6  | Overdue A/R                     | VAS_299_OverdueAR
 *  7  | clients                         | VAS_299_Clients
 *  8  | client                          | VAS_299_Client
 *  9  | expiring                        | VAS_299_Expiring
 * 10  | open opportunities              | VAS_299_OpenOpportunities
 * 11  | open opportunity                | VAS_299_OpenOpportunity
 * 12  | proposals                       | VAS_299_ProposalsCount
 * 13  | proposal                        | VAS_299_ProposalCount
 * 14  | invoices overdue                | VAS_299_InvoicesOverdue
 * 15  | invoice overdue                 | VAS_299_InvoiceOverdue
 * 16  | contracts · ends                | VAS_299_ContractsEnds
 * 17  | contract · ends                 | VAS_299_ContractEnds
 * 18  | No owner                        | VAS_299_NoOwner
 * 19  | Nothing here right now.         | VAS_299_NothingHere
 * 20  | Unable to load                  | VAS_299_UnableToLoad
 * 21  | Retry                           | VAS_299_Retry
 * 22  | of                              | VAS_299_Of
 * 23  | Showing                         | VAS_299_Showing
 * 24  | Previous page                   | VAS_299_PrevPage
 * 25  | Next page                       | VAS_299_NextPage
 * 26  | Close                           | VAS_299_Close
 * 27  | Customer details                | VAS_299_CustomerDetails
 * 28  | Tier                            | VAS_299_Tier
 * 29  | Segment                         | VAS_299_Segment
 * 30  | Owner                           | VAS_299_Owner
 * 31  | ARR                             | VAS_299_ARR
 * 32  | Open tickets                    | VAS_299_OpenTicketsFact
 * 33  | Projects                        | VAS_299_Projects
 * 34  | Pipeline                        | VAS_299_Pipeline
 * 35  | Onboarding                      | VAS_299_Onboarding
 * 36  | Key client                      | VAS_299_KeyClient
 * 37  | Signals                         | VAS_299_Signals
 * 38  | overdue                         | VAS_299_Overdue
 * 39  | days past due                   | VAS_299_DaysPastDue
 * 40  | open support tickets            | VAS_299_OpenSupportTickets
 * 41  | Key client — prioritise         | VAS_299_KeyPrioritise
 * 42  | Open support requests           | VAS_299_OpenRequests
 * 43  | Open record                     | VAS_299_OpenRecord
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Generic customer detail endpoint (built with VAS_126).
    var CUSTOMER_ENDPOINT = 'VAS_126_OpenTicketsWidget/';
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

    // Tile definitions in the required order. tone maps to the design system's
    // colour-coded accents (pipeline blue, proposals violet, contracts amber,
    // A/R red).
    var TILES = [
        { flag: 'pipeline', icon: 'trend', tone: 'info', labelKey: 'VAS_299_OpenPipeline', labelText: 'Open pipeline' },
        { flag: 'proposals', icon: 'doc', tone: 'violet', labelKey: 'VAS_299_OpenProposals', labelText: 'Open proposals' },
        { flag: 'contracts', icon: 'clock', tone: 'warn', labelKey: 'VAS_299_ContractsLE90', labelText: 'Contracts ≤90d' },
        { flag: 'overdue', icon: 'cash', tone: 'danger', labelKey: 'VAS_299_OverdueAR', labelText: 'Overdue A/R' }
    ];

    VAS.VAS_299_RevenueAtAGlanceWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas299-root">');
        var $grid;

        var widgetCurrency = { symbol: '', iso: '', precision: 2 };

        // Drill-down list modal state.
        var $list, $listBody, $listPager, $listTitle;
        var listFlag = 'pipeline', listOffset = 0, listTotal = 0, listSeq = 0;
        var listCurrency = { symbol: '', iso: '', precision: 2 };
        var LIST_PAGE = 7;

        // Customer detail modal state (reuses VAS_126 generic endpoint).
        var $detail, $detailBody, $detailSummary, currentDetailId = 0, currentDetailName = '';
        var detailCurrency = { symbol: '', iso: '', precision: 2 };

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
        function precisionOf(cur) {
            var p = Number(cur && cur.precision);
            if (!isNaN(p) && p >= 0) { return p; }
            if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                p = Number(VIS.Env.getCtx().getStdPrecision());
            }
            return !isNaN(p) && p >= 0 ? p : 0;
        }
        // Compact money against a base (accounting-schema) currency descriptor.
        function formatMoney(value, cur) {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '';
            var iso = (cur && cur.iso) || '';
            var symbol = (cur && (cur.symbol || cur.iso)) || '';
            return sign + symbol + VIS.Util.formatCompactAmount(n, iso, precisionOf(cur));
        }
        function projectText(name, count, dash) {
            if (!name) { return dash; }
            var extra = Number(count || 0) - 1;
            return escapeHtml(extra > 0 ? name + ' +' + formatCount(extra) : name);
        }

        function icon(name) {
            if (name === 'trend') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="22 7 13.5 15.5 8.5 10.5 2 17"></polyline><polyline points="16 7 22 7 22 13"></polyline></svg>'; }
            if (name === 'doc') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><path d="M14 2v6h6"></path></svg>'; }
            if (name === 'clock') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>'; }
            if (name === 'cash') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"></rect><circle cx="12" cy="12" r="2"></circle><path d="M6 12h.01M18 12h.01"></path></svg>'; }
            if (name === 'ticket') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v1a2 2 0 0 0 0 4v1a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-1a2 2 0 0 0 0-4z"></path><line x1="9" y1="7" x2="9" y2="17"></line></svg>'; }
            if (name === 'folder') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.93a2 2 0 0 1-1.66-.9l-.82-1.2A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13c0 1.1.9 2 2 2z"></path></svg>'; }
            if (name === 'arrow') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>'; }
            if (name === 'close') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>'; }
            if (name === 'chev') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>'; }
            if (name === 'chevL') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>'; }
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
            var cls = 'vas299-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas299-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas299-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas299-tag-info'; }
            return '<span class="vas299-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }

        /* ---------- Tiles ---------- */

        function tileHtml(tile) {
            return '<button type="button" class="vas299-tile MPC-tone-' + tile.tone + '" data-flag="' + tile.flag + '" tabindex="0">' +
                '<span class="vas299-tile-ic">' + icon(tile.icon) + '</span>' +
                '<span class="vas299-tile-val" data-val="' + tile.flag + '">…</span>' +
                '<span class="vas299-tile-lab">' + escapeHtml(label(tile.labelKey, tile.labelText)) + '</span>' +
                '<span class="vas299-tile-sub" data-sub="' + tile.flag + '"></span>' +
            '</button>';
        }

        function loadSummary() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_299_RevenueAtAGlanceWidget/GetSummary',
                type: 'GET', cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { showError(); return; }
                    widgetCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };

                    setTile('pipeline', data.pipelineValue, data.pipelineClients, 'VAS_299_Clients', 'VAS_299_Client', 'clients');
                    setTile('proposals', data.proposalValue, data.proposalClients, 'VAS_299_Clients', 'VAS_299_Client', 'clients');
                    setTile('contracts', data.contractValue, data.contractCount, null, null, 'expiring');
                    setTile('overdue', data.overdueValue, data.overdueClients, 'VAS_299_Clients', 'VAS_299_Client', 'clients');
                },
                error: showError
            });
        }

        // "expiring" always uses its own fixed suffix; the others pluralise
        // clients/client by count.
        function setTile(flag, value, count, pluralKey, singularKey, kind) {
            $grid.find('[data-val="' + flag + '"]').text(formatMoney(value, widgetCurrency));
            var n = Number(count || 0);
            var sub = kind === 'expiring'
                ? formatCount(n) + ' ' + label('VAS_299_Expiring', 'expiring')
                : formatCount(n) + ' ' + (n === 1 ? label(singularKey, 'client') : label(pluralKey, 'clients'));
            $grid.find('[data-sub="' + flag + '"]').text(sub);
        }

        function showError() {
            $grid.html('<div class="vas299-state">' + escapeHtml(label('VAS_299_UnableToLoad', 'Unable to load')) +
                ' <button type="button" class="vas299-retry">' + escapeHtml(label('VAS_299_Retry', 'Retry')) + '</button></div>');
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
            $('body').addClass('vas299-modal-open');
            loadList();
        }
        function closeList() {
            if (!$list) { return; }
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas299-modal-open'); }
        }
        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas299-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_299_RevenueAtAGlanceWidget/GetList',
                type: 'GET', cache: false,
                data: { flag: listFlag, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $listBody.html('<div class="vas299-state">' + escapeHtml(label('VAS_299_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    listTotal = Number(data.total || 0);
                    listCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
                    renderList(data.items || []);
                },
                error: function () { if (seq === listSeq) { $listBody.html('<div class="vas299-state">' + escapeHtml(label('VAS_299_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }

        function rowMeta(item) {
            var n = Number(item.count || 0);
            if (listFlag === 'pipeline') {
                return formatCount(n) + ' ' + (n === 1 ? label('VAS_299_OpenOpportunity', 'open opportunity') : label('VAS_299_OpenOpportunities', 'open opportunities'));
            }
            if (listFlag === 'proposals') {
                return formatCount(n) + ' ' + (n === 1 ? label('VAS_299_ProposalCount', 'proposal') : label('VAS_299_ProposalsCount', 'proposals'));
            }
            if (listFlag === 'contracts') {
                var word = n === 1 ? label('VAS_299_ContractEnds', 'contract · ends') : label('VAS_299_ContractsEnds', 'contracts · ends');
                return formatCount(n) + ' ' + word + ' ' + formatDate(item.endDate);
            }
            // overdue
            return formatCount(n) + ' ' + (n === 1 ? label('VAS_299_InvoiceOverdue', 'invoice overdue') : label('VAS_299_InvoicesOverdue', 'invoices overdue'));
        }

        function renderList(items) {
            if (!items.length) {
                $listBody.html('<div class="vas299-state">' + escapeHtml(label('VAS_299_NothingHere', 'Nothing here right now.')) + '</div>');
                $listPager.empty();
                return;
            }
            var rows = items.map(function (item) {
                var meta = rowMeta(item);
                return '<button type="button" class="vas299-row" data-id="' + Number(item.customerId) + '">' +
                    '<span class="vas299-row-main">' +
                        '<span class="vas299-row-title" title="' + escapeHtml(item.customerName) + '">' + escapeHtml(item.customerName) + '</span>' +
                        '<span class="vas299-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                    '</span>' +
                    '<span class="vas299-row-val">' + escapeHtml(formatMoney(item.amount, listCurrency)) + '</span>' +
                '</button>';
            }).join('');

            var start = listOffset + 1;
            var end = listOffset + items.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_299_Of', 'of');
            var helper = label('VAS_299_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listTotal);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;

            $listBody.html('<div class="vas299-list">' + rows + '</div>');
            $listPager.html(
                '<span class="vas299-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas299-pgctl">' +
                    '<button type="button" class="vas299-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_299_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas299-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas299-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_299_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
                '<div class="vas299-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas299-scrim" data-list-close></div>' +
                    '<section class="vas299-panel">' +
                        '<header class="vas299-phead"><h2 class="vas299-ptitle"></h2>' +
                            '<button type="button" class="vas299-close" data-list-close aria-label="' + escapeHtml(label('VAS_299_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas299-pbody"></div>' +
                        '<footer class="vas299-pfoot"><div class="vas299-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas299-pbody');
            $listPager = $list.find('.vas299-pager');
            $listTitle = $list.find('.vas299-ptitle');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '.vas299-row', function () { openCustomer(Number($(this).attr('data-id'))); });
            $list.on('click', '.vas299-pgbtn', function () { turnPage($(this).attr('data-dir')); });
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

        function anyModalOpen() {
            return ($list && $list.hasClass('is-open')) || ($detail && $detail.hasClass('is-open'));
        }

        function openCustomer(bpId) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas299-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas299-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas299-state">' + escapeHtml(label('VAS_299_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas299-state">' + escapeHtml(label('VAS_299_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas299-fact"><div class="vas299-fl">' + escapeHtml(fallback) + '</div><div class="vas299-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas299-signal"><span class="vas299-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas299-sig-main"><div class="vas299-sig-name">' + titleHtml + '</div><div class="vas299-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
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
            if (data.isKeyClient) { summaryParts.push(label('VAS_299_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas299-factgrid">' +
                fact(label('VAS_299_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_299_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_299_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_299_ARR', 'ARR'), escapeHtml(formatMoney(data.value, detailCurrency))) +
                fact(label('VAS_299_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_299_Projects', 'Projects'), projectText(data.projectName, projects, dash)) +
                fact(label('VAS_299_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatMoney(pipeline, detailCurrency)) : dash) +
                fact(label('VAS_299_Onboarding', 'Onboarding'), data.onboardingPercent == null ? dash : escapeHtml(formatCount(data.onboardingPercent) + '%')) +
            '</div>';

            var signals = '';
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatMoney(overdue, detailCurrency) + ' ' + label('VAS_299_Overdue', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_299_DaysPastDue', 'days past due'));
            }
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_299_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_299_KeyPrioritise', 'Key client — prioritise') : label('VAS_299_OpenRequests', 'Open support requests'));
            }
            var signalsBlock = signals ? '<div class="vas299-signals"><div class="vas299-sig-title">' + escapeHtml(label('VAS_299_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas299-id">' +
                '<span class="vas299-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas299-id-main"><div class="vas299-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas299-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas299-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas299-modal-open'); }
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
                '<div class="vas299-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_299_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas299-scrim" data-detail-close></div>' +
                    '<section class="vas299-dpanel">' +
                        '<header class="vas299-phead"><h2 class="vas299-ptitle">' + escapeHtml(label('VAS_299_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas299-phead-right"><span class="vas299-dsummary"></span>' +
                                '<button type="button" class="vas299-close" data-detail-close aria-label="' + escapeHtml(label('VAS_299_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas299-dbody"></div>' +
                        '<footer class="vas299-dfoot">' +
                            '<button type="button" class="vas299-btn vas299-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_299_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas299-dbody');
            $detailSummary = $detail.find('.vas299-dsummary');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-act="open"]', function () { var id = currentDetailId; zoomToCustomer(id); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas299-card">' +
                    '<div class="vas299-head">' +
                        '<span class="vas299-iconwell">' + icon('cash') + '</span>' +
                        '<div class="vas299-head-txt">' +
                            '<div class="vas299-title">' + escapeHtml(label('VAS_299_RevenueAtAGlance', 'Revenue at a glance')) + '</div>' +
                            '<div class="vas299-sub">' + escapeHtml(label('VAS_299_HeaderSub', 'Pipeline, proposals, contracts & A/R')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas299-grid">' + TILES.map(tileHtml).join('') + '</div>' +
                '</div>'
            );
            $grid = $card.find('.vas299-grid');
            $card.on('click', '.vas299-tile', function () { openList($(this).attr('data-flag')); });
            $card.on('keydown', '.vas299-tile', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); openList($(this).attr('data-flag')); }
            });
            $card.on('click', '.vas299-retry', function () { $grid.html(TILES.map(tileHtml).join('')); loadSummary(); });
            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createListDialog();
            createDetailDialog();
            $(document).on('keydown.MPCvas299', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); }
                else if ($list && $list.hasClass('is-open')) { closeList(); }
            });
            loadSummary();
        };

        this.refreshWidget = function () { loadSummary(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas299');
            if ($list) { $list.remove(); $list = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            $('body').removeClass('vas299-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_299_RevenueAtAGlanceWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_299_RevenueAtAGlanceWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_299_RevenueAtAGlanceWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_299_RevenueAtAGlanceWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_299_RevenueAtAGlanceWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_299_RevenueAtAGlanceWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
