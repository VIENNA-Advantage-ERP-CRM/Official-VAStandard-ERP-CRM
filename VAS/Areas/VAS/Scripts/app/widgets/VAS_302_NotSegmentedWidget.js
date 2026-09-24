/**
 * VAS_302 Not Segmented Yet Widget (Customers module dashboard)
 * Purpose - 3x2 glass list: active customers with no active membership in any
 *           target list (segment), ranked by ARR desc so the highest-value
 *           gaps surface first. Each row shows a tier tag, ARR and owner, and
 *           a "Segment" action that assigns just that one customer inline.
 *           Header shows "{n} not in any target list" and a "Segment all ->"
 *           link that opens a bulk checklist of the top unsegmented
 *           customers. Row click opens the customer detail modal (shared
 *           with VAS_126/138/295-298). Dedicated, actionable counterpart to
 *           the "Not segmented" tile on VAS_137's Needs Attention widget.
 *
 * Design  - not-segmented.html (attached) + Design Specs/dashboard-widgets.md
 *           "Grid Data Widget". Glass surface, icon-well header, ranked rows
 *           (paged 7), circular pager. Internal sizing in em against the
 *           widget-root clamp; borders/radii in px. CSS namespaced vas302-*
 *           (MPC prefix rule).
 *
 * Backend - VAS_302_NotSegmentedWidget/GetRows (paged unsegmented customer
 *           rows + tier + total - the row list needs tier, which VAS_141's
 *           own GetUnsegmented does not return, so this widget owns its own
 *           read of the same "unsegmented" population plus tier resolution).
 *           Customer detail reuses the generic VAS_126 endpoint:
 *             VAS_126_OpenTicketsWidget/GetCustomerDetail
 *           The segment-selector list and the single/bulk assignment WRITE
 *           are NOT duplicated here - both the "Segment" row action and the
 *           "Segment all ->" bulk modal call VAS_141's own endpoints, since
 *           VAS_141 already implements this exact assignment feature as its
 *           own "Segment ->" bulk-assign side action:
 *             VAS_141_CustomersBySegmentWidget/GetUnsegmented  (segment list + bulk candidates)
 *             VAS_141_CustomersBySegmentWidget/AssignSegment   (the write)
 *           One write path for "assign a customer to a target list", not two.
 *
 * Routing - hosted on a window, opening the customer navigates the host grid
 *           in place (widgetFirevalueChanged); on the Home / landing
 *           dashboard (windowNo < 0) there is no host grid, so the record is
 *           opened in the standard Customer window via VAS.ZoomUtil.
 *           Matches VAS_135/138/141/295-301.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                       | Message Key
 * ----+-------------------------------------+--------------------------------
 *  1  | Not segmented yet                  | VAS_302_NotSegmentedYet
 *  2  | not in any target list             | VAS_302_NotInAnyList
 *  3  | Segment all                        | VAS_302_SegmentAll
 *  4  | Segment                            | VAS_302_Segment
 *  5  | ARR                                | VAS_302_ARR
 *  6  | No owner                           | VAS_302_NoOwner
 *  7  | Nothing here right now.            | VAS_302_NothingHere
 *  8  | Unable to load                     | VAS_302_UnableToLoad
 *  9  | Retry                              | VAS_302_Retry
 * 10  | of                                 | VAS_302_Of
 * 11  | Showing                            | VAS_302_Showing
 * 12  | Previous page                      | VAS_302_PrevPage
 * 13  | Next page                          | VAS_302_NextPage
 * 14  | Close                              | VAS_302_Close
 * 15  | Customer details                   | VAS_302_CustomerDetails
 * 16  | Tier                               | VAS_302_Tier
 * 17  | Segment                            | VAS_302_SegmentFact
 * 18  | Owner                              | VAS_302_Owner
 * 19  | Open tickets                       | VAS_302_OpenTicketsFact
 * 20  | Key client                         | VAS_302_KeyClient
 * 21  | Open record                        | VAS_302_OpenRecord
 * 22  | Add to segment                     | VAS_302_AddToSegment
 * 23  | Target segment                     | VAS_302_TargetSegment
 * 24  | Assign this customer to a target list. | VAS_302_AssignOneHint
 * 25  | Cancel                             | VAS_302_Cancel
 * 26  | Segment customers                  | VAS_302_SegmentCustomers
 * 27  | unsegmented                        | VAS_302_UnsegmentedCount
 * 28  | Add selected to segment            | VAS_302_AddSelectedToSegment
 * 29  | Top unsegmented by ARR             | VAS_302_TopUnsegmented
 * 30  | Select at least one customer.      | VAS_302_SelectAtLeastOne
 * 31  | Choose a target segment.           | VAS_302_ChooseSegment
 * 32  | Added to segment.                  | VAS_302_AddedToast
 * 33  | Customers segmented.               | VAS_302_BulkAddedToast
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Generic customer detail endpoint (built with VAS_126).
    var CUSTOMER_ENDPOINT = 'VAS_126_OpenTicketsWidget/';
    // Segment selector + assignment write, reused from VAS_141.
    var SEGMENT_ENDPOINT = 'VAS_141_CustomersBySegmentWidget/';
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

    VAS.VAS_302_NotSegmentedWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas302-root">');
        var $sub, $body;

        var pageSize = 7;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;

        // Detail modal state (reuses VAS_126 generic endpoint).
        var $detail, $detailBody, $detailSummary, currentDetailId = 0;
        var detailCurrency = { symbol: '', iso: '', precision: 2 };

        // Segment-selector cache (reused across the single and bulk modals).
        var segmentsCache = null;

        // Single-row "Segment" modal state.
        var $one, $oneBody, oneCustId = 0, oneSeq = 0;

        // "Segment all" bulk modal state.
        var $bulk, $bulkBody, bulkSeq = 0;

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
        function tierTagHtml(tier) {
            if (!tier) { return ''; }
            var cls = 'vas302-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas302-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas302-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas302-tag-info'; }
            return '<span class="vas302-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }

        function icon(name) {
            if (name === 'target') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><circle cx="12" cy="12" r="6"></circle><circle cx="12" cy="12" r="2"></circle></svg>';
            }
            if (name === 'ticket') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v1a2 2 0 0 0 0 4v1a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-1a2 2 0 0 0 0-4z"></path><line x1="9" y1="7" x2="9" y2="17"></line></svg>';
            }
            if (name === 'cash') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"></rect><circle cx="12" cy="12" r="2"></circle><path d="M6 12h.01M18 12h.01"></path></svg>';
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

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_302_NotSegmentedWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    listTotal = Number(data.total || 0);
                    $sub.text(formatCount(listTotal) + ' ' + label('VAS_302_NotInAnyList', 'not in any target list'));
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) { $body.html('<div class="vas302-state">' + escapeHtml(message) + '</div>'); }
        function renderErrorState() {
            $body.html('<div class="vas302-state">' + escapeHtml(label('VAS_302_UnableToLoad', 'Unable to load')) +
                ' <button type="button" class="vas302-retry">' + escapeHtml(label('VAS_302_Retry', 'Retry')) + '</button></div>');
        }

        function rowHtml(item) {
            var name = item.customerName || '';
            var owner = item.ownerName || label('VAS_302_NoOwner', 'No owner');
            return '<div class="vas302-row" data-id="' + Number(item.customerId) + '" data-name="' + escapeHtml(name) + '">' +
                '<span class="vas302-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<span class="vas302-row-main">' +
                    '<span class="vas302-row-title" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                    '<span class="vas302-row-meta">' + tierTagHtml(item.tier) + '<span>' + escapeHtml(formatMoney(item.arr, { precision: 0 })) + ' ' + escapeHtml(label('VAS_302_ARR', 'ARR')) + ' · ' + escapeHtml(owner) + '</span></span>' +
                '</span>' +
                '<button type="button" class="vas302-segbtn" data-segid="' + Number(item.customerId) + '" data-segname="' + escapeHtml(name) + '">' + icon('target') + escapeHtml(label('VAS_302_Segment', 'Segment')) + '</button>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) { renderState(label('VAS_302_NothingHere', 'Nothing here right now.')); return; }
            var rows = items.map(rowHtml).join('');
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas302-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + rows + '</div>' + pagerHtml(pageOffset, pageSize, listTotal));
        }

        /* Footer pager (Design Specs/dashboard-widgets.md §"Widget Footer Pager"):
           "Showing X–Y of Z" helper on the left, compact prev · "N of M" · next
           control on the right. */
        function pagerHtml(offset, size, total) {
            var start = offset + 1;
            var pages = Math.max(1, Math.ceil(total / size));
            var current = Math.floor(offset / size);
            var end = Math.min(offset + size, total);
            var of = label('VAS_302_Of', 'of');
            var helper = label('VAS_302_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(total);
            var pageText = (current + 1) + ' ' + of + ' ' + pages;
            return '<div class="vas302-pager">' +
                '<span class="vas302-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas302-pgctl">' +
                    '<button type="button" class="vas302-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_302_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas302-pgtext">' + escapeHtml(pageText) + '</span>' +
                    '<button type="button" class="vas302-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_302_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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

        function anyModalOpen() {
            return ($detail && $detail.hasClass('is-open')) || ($one && $one.hasClass('is-open')) || ($bulk && $bulk.hasClass('is-open'));
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

        function openCustomer(bpId) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas302-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas302-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas302-state">' + escapeHtml(label('VAS_302_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas302-state">' + escapeHtml(label('VAS_302_UnableToLoad', 'Unable to load')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas302-fact"><div class="vas302-fl">' + escapeHtml(fallback) + '</div><div class="vas302-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas302-signal"><span class="vas302-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas302-sig-main"><div class="vas302-sig-name">' + titleHtml + '</div><div class="vas302-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
        }

        function renderCustomerDetail(data) {
            detailCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
            var dash = '—';
            var name = data.name || '';
            var sub = [data.contactName, data.contactEmail].filter(function (p) { return p; }).join(' · ');
            var tierLabel = data.tier || data.tierCode || '';
            var summaryParts = [];
            if (tierLabel) { summaryParts.push(tierLabel); }
            if (data.isKeyClient) { summaryParts.push(label('VAS_302_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var facts = '<div class="vas302-factgrid">' +
                fact(label('VAS_302_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_302_SegmentFact', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_302_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_302_ARR', 'ARR'), escapeHtml(formatMoney(data.value, detailCurrency))) +
                fact(label('VAS_302_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
            '</div>';

            var signals = '';
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatMoney(overdue, detailCurrency)), '');
            }
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_302_OpenTicketsFact', 'Open tickets')), '');
            }
            var signalsBlock = signals ? '<div class="vas302-signals">' + signals + '</div>' : '';

            var identity = '<div class="vas302-id">' +
                '<span class="vas302-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas302-id-main"><div class="vas302-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas302-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas302-modal-open'); }
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
                '<div class="vas302-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_302_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas302-scrim" data-detail-close></div>' +
                    '<section class="vas302-dpanel">' +
                        '<header class="vas302-phead"><h2 class="vas302-ptitle">' + escapeHtml(label('VAS_302_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas302-phead-right"><span class="vas302-dsummary"></span>' +
                                '<button type="button" class="vas302-close" data-detail-close aria-label="' + escapeHtml(label('VAS_302_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas302-dbody"></div>' +
                        '<footer class="vas302-dfoot">' +
                            '<button type="button" class="vas302-btn vas302-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_302_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas302-dbody');
            $detailSummary = $detail.find('.vas302-dsummary');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-act="open"]', function () { var id = currentDetailId; closeDetail(); zoomToCustomer(id); });
        }

        /* ---------- Segment selector (reuses VAS_141's GetUnsegmented) ---------- */

        function ensureSegments(callback) {
            if (segmentsCache) { callback(segmentsCache); return; }
            $.ajax({
                url: VIS.Application.contextUrl + SEGMENT_ENDPOINT + 'GetUnsegmented',
                type: 'GET', cache: false, data: { offset: 0, limit: 1 },
                success: function (response) {
                    var data = parseResponse(response);
                    segmentsCache = (data && data.segments) || [];
                    callback(segmentsCache);
                },
                error: function () { callback([]); }
            });
        }
        function segmentOptionsHtml(segments) {
            if (!segments.length) { return ''; }
            return segments.map(function (s) { return '<option value="' + Number(s.id) + '">' + escapeHtml(s.name) + '</option>'; }).join('');
        }

        /* ---------- Segment-one modal (write reused from VAS_141) ---------- */

        function openSegmentOne(bpId, custName) {
            if (!bpId) { return; }
            oneCustId = bpId;
            $one.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas302-modal-open');
            $one.find('.vas302-ptitle').text(label('VAS_302_AddToSegment', 'Add to segment'));
            $oneBody.html(
                '<div class="vas302-onehint">' + escapeHtml(label('VAS_302_AssignOneHint', 'Assign this customer to a target list.').replace('this customer', custName || 'this customer')) + '</div>' +
                '<div class="vas302-field"><label>' + escapeHtml(label('VAS_302_TargetSegment', 'Target segment')) + '</label><select class="vas302-segselect"></select></div>'
            );
            ensureSegments(function (segments) {
                $oneBody.find('.vas302-segselect').html(segmentOptionsHtml(segments));
            });
        }
        function closeOne() {
            if (!$one) { return; }
            $one.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas302-modal-open'); }
        }
        function doSegmentOne() {
            var segId = Number($oneBody.find('.vas302-segselect').val() || 0);
            if (!segId) { toast(label('VAS_302_ChooseSegment', 'Choose a target segment.')); return; }
            var seq = ++oneSeq;
            $.ajax({
                url: VIS.Application.contextUrl + SEGMENT_ENDPOINT + 'AssignSegment',
                type: 'POST', cache: false,
                data: { C_MasterTargetList_ID: segId, customerIds: String(oneCustId) },
                success: function (response) {
                    if (seq !== oneSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { toast(data.error); return; }
                    closeOne();
                    toast(label('VAS_302_AddedToast', 'Added to segment.'));
                    pageOffset = 0;
                    loadRows();
                },
                error: function () { if (seq === oneSeq) { toast(label('VAS_302_UnableToLoad', 'Unable to load')); } }
            });
        }

        /* ---------- Segment-all bulk modal (reuses VAS_141's GetUnsegmented/AssignSegment) ---------- */

        function openSegmentBulk() {
            $bulk.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas302-modal-open');
            $bulkBody.html('<div class="vas302-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            var seq = ++bulkSeq;
            $.ajax({
                url: VIS.Application.contextUrl + SEGMENT_ENDPOINT + 'GetUnsegmented',
                type: 'GET', cache: false, data: { offset: 0, limit: 12 },
                success: function (response) {
                    if (seq !== bulkSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $bulkBody.html('<div class="vas302-state">' + escapeHtml(label('VAS_302_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    segmentsCache = data.segments || segmentsCache;
                    renderBulk(data);
                },
                error: function () { if (seq === bulkSeq) { $bulkBody.html('<div class="vas302-state">' + escapeHtml(label('VAS_302_UnableToLoad', 'Unable to load')) + '</div>'); } }
            });
        }
        function closeBulk() {
            if (!$bulk) { return; }
            $bulk.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas302-modal-open'); }
        }
        function bulkRowHtml(c) {
            var name = c.customerName || '';
            return '<label class="vas302-chk"><input type="checkbox" checked value="' + Number(c.customerId) + '"/>' +
                '<span class="vas302-avatar" style="width:28px;height:28px;background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<span class="vas302-row-main"><span class="vas302-row-title" style="font-size:.85em">' + escapeHtml(name) + '</span>' +
                    '<span class="vas302-row-meta"><span>' + escapeHtml(formatMoney(c.customerValue, { precision: 0 })) + '</span></span></span></label>';
        }
        function renderBulk(data) {
            var customers = data.customers || [];
            var total = Number(data.total || 0);
            $bulk.find('.vas302-ptitle').text(label('VAS_302_SegmentCustomers', 'Segment customers'));
            $bulk.find('.vas302-bsummary').text(formatCount(total) + ' ' + label('VAS_302_UnsegmentedCount', 'unsegmented'));
            var rows = customers.length
                ? customers.map(bulkRowHtml).join('')
                : '<div class="vas302-state">' + escapeHtml(label('VAS_302_NothingHere', 'Nothing here right now.')) + '</div>';
            $bulkBody.html(
                '<div class="vas302-field"><label>' + escapeHtml(label('VAS_302_AddSelectedToSegment', 'Add selected to segment')) + '</label><select class="vas302-segselect">' + segmentOptionsHtml(segmentsCache || []) + '</select></div>' +
                '<div class="vas302-sectitle">' + escapeHtml(label('VAS_302_TopUnsegmented', 'Top unsegmented by ARR')) + '</div>' +
                '<div class="vas302-chklist">' + rows + '</div>'
            );
        }
        function doSegmentBulk() {
            var segId = Number($bulkBody.find('.vas302-segselect').val() || 0);
            if (!segId) { toast(label('VAS_302_ChooseSegment', 'Choose a target segment.')); return; }
            var ids = $bulkBody.find('.vas302-chklist input:checked').map(function () { return $(this).val(); }).get();
            if (!ids.length) { toast(label('VAS_302_SelectAtLeastOne', 'Select at least one customer.')); return; }
            var seq = ++bulkSeq;
            $.ajax({
                url: VIS.Application.contextUrl + SEGMENT_ENDPOINT + 'AssignSegment',
                type: 'POST', cache: false,
                data: { C_MasterTargetList_ID: segId, customerIds: ids.join(',') },
                success: function (response) {
                    if (seq !== bulkSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { toast(data.error); return; }
                    closeBulk();
                    toast(label('VAS_302_BulkAddedToast', 'Customers segmented.'));
                    pageOffset = 0;
                    loadRows();
                },
                error: function () { if (seq === bulkSeq) { toast(label('VAS_302_UnableToLoad', 'Unable to load')); } }
            });
        }

        /* ---------- Toast ---------- */

        var toastTimer = null;
        function toast(message) {
            var $toast = $root.data('vas302-toast');
            if (!$toast) {
                $toast = $('<div class="vas302-toast"></div>');
                $('body').append($toast);
                $root.data('vas302-toast', $toast);
            }
            $toast.text(message).addClass('is-show');
            if (toastTimer) { clearTimeout(toastTimer); }
            toastTimer = setTimeout(function () { $toast.removeClass('is-show'); }, 2200);
        }

        function createOneDialog() {
            $one = $(
                '<div class="vas302-onedlg" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas302-scrim" data-one-close></div>' +
                    '<section class="vas302-opanel">' +
                        '<header class="vas302-phead"><h2 class="vas302-ptitle"></h2>' +
                            '<button type="button" class="vas302-close" data-one-close aria-label="' + escapeHtml(label('VAS_302_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas302-obody"></div>' +
                        '<footer class="vas302-dfoot">' +
                            '<button type="button" class="vas302-btn vas302-btn-ghost" data-one-close>' + escapeHtml(label('VAS_302_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas302-btn vas302-btn-primary" data-one-save>' + icon('target') + escapeHtml(label('VAS_302_AddToSegment', 'Add to segment')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($one);
            $oneBody = $one.find('.vas302-obody');
            $one.on('click', '[data-one-close]', closeOne);
            $one.on('click', '[data-one-save]', doSegmentOne);
        }

        function createBulkDialog() {
            $bulk = $(
                '<div class="vas302-bulkdlg" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas302-scrim" data-bulk-close></div>' +
                    '<section class="vas302-bpanel">' +
                        '<header class="vas302-phead"><h2 class="vas302-ptitle"></h2>' +
                            '<div class="vas302-phead-right"><span class="vas302-bsummary"></span>' +
                                '<button type="button" class="vas302-close" data-bulk-close aria-label="' + escapeHtml(label('VAS_302_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas302-bbody"></div>' +
                        '<footer class="vas302-dfoot">' +
                            '<button type="button" class="vas302-btn vas302-btn-ghost" data-bulk-close>' + escapeHtml(label('VAS_302_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas302-btn vas302-btn-primary" data-bulk-save>' + icon('target') + escapeHtml(label('VAS_302_AddToSegment', 'Add to segment')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($bulk);
            $bulkBody = $bulk.find('.vas302-bbody');
            $bulk.on('click', '[data-bulk-close]', closeBulk);
            $bulk.on('click', '[data-bulk-save]', doSegmentBulk);
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas302-card">' +
                    '<div class="vas302-head">' +
                        '<div class="vas302-head-l">' +
                            '<span class="vas302-iconwell">' + icon('target') + '</span>' +
                            '<div class="vas302-head-txt"><div class="vas302-title">' + escapeHtml(label('VAS_302_NotSegmentedYet', 'Not segmented yet')) + '</div>' +
                                '<div class="vas302-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas302-alllink">' + escapeHtml(label('VAS_302_SegmentAll', 'Segment all')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas302-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas302-sub');
            $body = $card.find('.vas302-body');

            $card.on('click', '.vas302-alllink', function () { openSegmentBulk(); });
            $card.on('click', '.vas302-row', function () { openCustomer(Number($(this).attr('data-id'))); });
            $card.on('click', '.vas302-segbtn', function (e) {
                e.stopPropagation();
                openSegmentOne(Number($(this).attr('data-segid')), $(this).attr('data-segname'));
            });
            $card.on('click', '.vas302-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $card.on('click', '.vas302-retry', function () { loadRows(); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createDetailDialog();
            createOneDialog();
            createBulkDialog();
            $(document).on('keydown.MPCvas302', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($bulk && $bulk.hasClass('is-open')) { closeBulk(); return; }
                if ($one && $one.hasClass('is-open')) { closeOne(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); }
            });
            loadRows();
        };

        this.refreshWidget = function () { pageOffset = 0; loadRows(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas302');
            if ($detail) { $detail.remove(); $detail = null; }
            if ($one) { $one.remove(); $one = null; }
            if ($bulk) { $bulk.remove(); $bulk = null; }
            var $toast = $root.data('vas302-toast');
            if ($toast) { $toast.remove(); }
            $('body').removeClass('vas302-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_302_NotSegmentedWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_302_NotSegmentedWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_302_NotSegmentedWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_302_NotSegmentedWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_302_NotSegmentedWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_302_NotSegmentedWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
