/**
 * VAS_141 Customers by Segment Widget (Customers module dashboard)
 * Purpose - 3x1 glass distribution: active customers grouped by segment (Onfinity
 *           target list). Header shows "{segmented} segmented · {unsegmented}
 *           unsegmented" and a "Segment ->" link. Each row is a target list with a
 *           coloured swatch, its distinct customer count and a share bar (share of
 *           the segmented total). Clicking a segment opens its customers (ranked by
 *           value); "Segment ->" opens the bulk-assign modal (top unsegmented +
 *           target-list selector).
 * Design  - customers-by-segment.html (attached) + Design Specs/dashboard-widgets.md.
 *           Glass surface, icon-well header, coloured segment rows + distribution
 *           bars; drill-down list + bulk-assign modals. Internal sizing in em against
 *           the widget-root clamp; borders/strokes in px. CSS namespaced vas141-*.
 *
 * Backend - VAS_141_CustomersBySegmentWidget/GetSegments          (distribution + totals)
 *           VAS_141_CustomersBySegmentWidget/GetSegmentCustomers  (one segment's customers)
 *           VAS_141_CustomersBySegmentWidget/GetUnsegmented       (unsegmented + segment list)
 *           VAS_141_CustomersBySegmentWidget/AssignSegment        (bulk assignment)
 *
 * Routing - 2026-08-17: hosted on a window, opening the customer navigates the host
 *           grid in place (widgetFirevalueChanged); on the Home / landing dashboard
 *           (windowNo < 0) there is no host grid, so the record is opened in the
 *           standard Customer window via VAS.ZoomUtil.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                       | Message Key
 * ----+------------------------------------+--------------------------------
 *  1  | Customers by segment               | VAS_141_CustomersBySegment
 *  2  | segmented                          | VAS_141_Segmented
 *  3  | unsegmented                        | VAS_141_Unsegmented
 *  4  | Segment                            | VAS_141_SegmentLink
 *  5  | of                                 | VAS_141_Of
 * 5a  | Showing                            | VAS_141_Showing
 * 5b  | Previous page                      | VAS_141_PrevPage
 * 5c  | Next page                          | VAS_141_NextPage
 *  6  | Nothing here right now.            | VAS_141_NothingHere
 *  7  | No customers.                      | VAS_141_NoCustomers
 *  8  | No target lists are configured.    | VAS_141_NoTargetLists
 *  9  | All active customers are segmented.| VAS_141_AllSegmented
 * 10  | Unable to load segments.           | VAS_141_UnableToLoad
 * 11  | Retry                              | VAS_141_Retry
 * 12  | Segment ·                          | VAS_141_SegmentTitle (prefix)
 * 13  | Segment customers                  | VAS_141_BulkTitle
 * 14  | Add selected to segment            | VAS_141_AddToSegment
 * 15  | Top unsegmented by value           | VAS_141_TopUnsegmented
 * 16  | Cancel                             | VAS_141_Cancel
 * 17  | Add to segment                     | VAS_141_AddButton
 * 18  | Close                              | VAS_141_Close
 * 19  | No owner                           | VAS_141_NoOwner
 * 20  | Assigned {n} customer(s).          | VAS_141_Assigned
 * 21  | Already assigned                   | VAS_141_AlreadyAssigned
 * 22  | Could not assign.                  | VAS_141_AssignFailed
 * 23  | Select a target list.              | VAS_141_SelectList
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

    var SEG_COLORS = ['#0083DA', '#5F4AA6', '#0B6B45', '#D78B10', '#A33F3F', '#106AB0', '#41576A'];

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

    VAS.VAS_141_CustomersBySegmentWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas141-root">');
        var $sub, $body;

        var hasSegments = false, hasTargetLists = true;

        // Distribution pagination (client-side over the returned segments).
        var allSegments = [], segPage = 0, segPageSize = 5;

        // Segment drill-down modal state.
        var $list, $listBody, $listPager, $listTitle;
        var listSegId = 0, listOffset = 0, listTotal = 0, listSeq = 0;
        var listCurrency = { symbol: '', iso: '', precision: 2 };
        var LIST_PAGE = 7;

        // Bulk-assign modal state.
        var $bulk, $bulkBody, $bulkSelect, $bulkError, $bulkSave;
        var bulkCurrency = { symbol: '', iso: '', precision: 2 };

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
            if (name === 'layers') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>';
            }
            if (name === 'target') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><circle cx="12" cy="12" r="6"></circle><circle cx="12" cy="12" r="2"></circle></svg>';
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

        /* ---------- Distribution ---------- */

        function loadSegments() {
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_141_CustomersBySegmentWidget/GetSegments',
                type: 'GET', cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { renderError(); return; }
                    var segmented = Number(data.segmented || 0);
                    var unsegmented = Number(data.unsegmented || 0);
                    $sub.text(formatCount(segmented) + ' ' + label('VAS_141_Segmented', 'segmented') + ' · ' + formatCount(unsegmented) + ' ' + label('VAS_141_Unsegmented', 'unsegmented'));
                    renderSegments(data.segments || []);
                },
                error: renderError
            });
        }

        function renderState(message) { $body.html('<div class="vas141-state">' + escapeHtml(message) + '</div>'); }
        function renderError() {
            $body.html('<div class="vas141-state">' + escapeHtml(label('VAS_141_UnableToLoad', 'Unable to load segments.')) +
                ' <button type="button" class="vas141-retry">' + escapeHtml(label('VAS_141_Retry', 'Retry')) + '</button></div>');
        }

        function segRowHtml(seg, i) {
            var color = SEG_COLORS[i % SEG_COLORS.length];
            var share = Math.max(0, Math.min(100, Number(seg.sharePercent || 0)));
            return '<div class="vas141-seg">' +
                '<div class="vas141-seg-top">' +
                    '<button type="button" class="vas141-seg-l" data-id="' + Number(seg.id) + '" data-name="' + escapeHtml(seg.name) + '">' +
                        '<span class="vas141-sw" style="background:' + color + '"></span>' +
                        '<span class="vas141-seg-name" title="' + escapeHtml(seg.name) + '">' + escapeHtml(seg.name) + '</span>' +
                    '</button>' +
                    '<span class="vas141-seg-v">' + formatCount(seg.customerCount) + '</span>' +
                '</div>' +
                '<div class="vas141-bar"><div style="width:' + share + '%;background:' + color + '"></div></div>' +
            '</div>';
        }

        function renderSegments(segments) {
            allSegments = segments || [];
            hasSegments = allSegments.length > 0;
            segPage = 0;
            if (!allSegments.length) {
                renderState(label('VAS_141_NothingHere', 'Nothing here right now.'));
                return;
            }
            renderSegPage();
        }

        // Renders the current page of segments (5/page) plus a circular pager when
        // there is more than one page. The colour index stays tied to the segment's
        // absolute position so each segment keeps its colour across pages.
        function renderSegPage() {
            var total = allSegments.length;
            var pages = Math.max(1, Math.ceil(total / segPageSize));
            if (segPage > pages - 1) { segPage = pages - 1; }
            if (segPage < 0) { segPage = 0; }
            var start = segPage * segPageSize;
            var slice = allSegments.slice(start, start + segPageSize);
            var rows = slice.map(function (seg, idx) { return segRowHtml(seg, start + idx); }).join('');
            // Always split the body into `segPageSize` equal-height tracks so each
            // record is body/pageSize tall and rows fill top-to-bottom; a partial page
            // occupies the first tracks and leaves the rest empty (no stretching).
            var html = '<div class="vas141-seglist" style="grid-template-rows: repeat(' + segPageSize + ', minmax(0, 1fr))">' + rows + '</div>';
            if (pages > 1) {
                /* Footer pager (dashboard-widgets.md §"Widget Footer Pager"):
                   helper left, compact prev · "N of M" · next right. */
                var from = start + 1, to = start + slice.length;
                var segOf = label('VAS_141_Of', 'of');
                var segHelper = label('VAS_141_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + segOf + ' ' + formatCount(total);
                html += '<div class="vas141-wpager">' +
                    '<span class="vas141-wpglabel">' + escapeHtml(segHelper) + '</span>' +
                    '<span class="vas141-wpgctl">' +
                        '<button type="button" class="vas141-wpgbtn" data-segdir="prev" aria-label="' + escapeHtml(label('VAS_141_PrevPage', 'Previous page')) + '" ' + (segPage <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                        '<span class="vas141-wpgtext">' + escapeHtml((segPage + 1) + ' ' + segOf + ' ' + pages) + '</span>' +
                        '<button type="button" class="vas141-wpgbtn" data-segdir="next" aria-label="' + escapeHtml(label('VAS_141_NextPage', 'Next page')) + '" ' + (segPage >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                    '</span>' +
                '</div>';
            }
            $body.html(html);
        }

        function turnSegPage(dir) {
            var pages = Math.max(1, Math.ceil(allSegments.length / segPageSize));
            if (dir === 'next' && segPage < pages - 1) { segPage++; }
            else if (dir === 'prev' && segPage > 0) { segPage--; }
            else { return; }
            renderSegPage();
        }

        /* ---------- Segment drill-down modal ---------- */

        function anyModalOpen() {
            return ($list && $list.hasClass('is-open')) || ($bulk && $bulk.hasClass('is-open'));
        }

        function openSegList(segId, segName) {
            if (!segId) { return; }
            listSegId = segId;
            listOffset = 0;
            $listTitle.text(label('VAS_141_SegmentTitle', 'Segment') + ' · ' + (segName || ''));
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas141-modal-open');
            loadList();
        }
        function closeSegList() {
            if (!$list) { return; }
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas141-modal-open'); }
        }
        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas141-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_141_CustomersBySegmentWidget/GetSegmentCustomers',
                type: 'GET', cache: false,
                data: { C_MasterTargetList_ID: listSegId, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $listBody.html('<div class="vas141-state">' + escapeHtml(label('VAS_141_UnableToLoad', 'Unable to load segments.')) + '</div>'); return; }
                    listTotal = Number(data.total || 0);
                    listCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
                    renderListRows(data.items || []);
                },
                error: function () { if (seq === listSeq) { $listBody.html('<div class="vas141-state">' + escapeHtml(label('VAS_141_UnableToLoad', 'Unable to load segments.')) + '</div>'); } }
            });
        }
        function custRowHtml(item) {
            var name = item.customerName || '';
            var meta = [item.ownerName || label('VAS_141_NoOwner', 'No owner'), item.email].filter(function (p) { return p; }).join(' · ');
            return '<button type="button" class="vas141-row" data-id="' + Number(item.customerId) + '">' +
                '<span class="vas141-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<span class="vas141-row-main">' +
                    '<span class="vas141-row-title" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                    '<span class="vas141-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas141-row-val">' + escapeHtml(formatMoney(item.customerValue, listCurrency)) + '</span>' +
            '</button>';
        }
        function renderListRows(items) {
            if (!items.length) {
                $listBody.html('<div class="vas141-state">' + escapeHtml(label('VAS_141_NoCustomers', 'No customers.')) + '</div>');
                $listPager.empty();
                return;
            }
            $listBody.html('<div class="vas141-list">' + items.map(custRowHtml).join('') + '</div>');
            var start = listOffset + 1, end = listOffset + items.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            /* Footer pager (dashboard-widgets.md §"Widget Footer Pager"): helper
               left, compact prev · "N of M" · next right. The control stays put
               on a single page with both arrows disabled. */
            var of = label('VAS_141_Of', 'of');
            var helper = label('VAS_141_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listTotal);
            $listPager.html(
                '<span class="vas141-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas141-pgctl">' +
                    '<button type="button" class="vas141-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_141_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas141-pgtext">' + escapeHtml((current + 1) + ' ' + of + ' ' + pages) + '</span>' +
                    '<button type="button" class="vas141-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_141_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
            closeSegList();
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
                '<div class="vas141-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas141-scrim" data-list-close></div>' +
                    '<section class="vas141-panel">' +
                        '<header class="vas141-phead"><h2 class="vas141-ptitle"></h2>' +
                            '<button type="button" class="vas141-close" data-list-close aria-label="' + escapeHtml(label('VAS_141_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas141-pbody"></div>' +
                        '<footer class="vas141-pfoot"><div class="vas141-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas141-pbody');
            $listPager = $list.find('.vas141-pager');
            $listTitle = $list.find('.vas141-ptitle');
            $list.on('click', '[data-list-close]', closeSegList);
            $list.on('click', '.vas141-row', function () { zoomToCustomer(Number($(this).attr('data-id'))); });
            $list.on('click', '.vas141-pgbtn', function () { turnPage($(this).attr('data-dir')); });
        }

        /* ---------- Bulk-assign modal ---------- */

        function openBulk() {
            $bulk.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas141-modal-open');
            $bulkError.hide().text('');
            $bulkBody.html('<div class="vas141-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $bulkSelect.html('');
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_141_CustomersBySegmentWidget/GetUnsegmented',
                type: 'GET', cache: false, data: { offset: 0, limit: 12 },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $bulkBody.html('<div class="vas141-state">' + escapeHtml(label('VAS_141_UnableToLoad', 'Unable to load segments.')) + '</div>'); return; }
                    bulkCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
                    renderBulk(data.customers || [], data.segments || []);
                },
                error: function () { $bulkBody.html('<div class="vas141-state">' + escapeHtml(label('VAS_141_UnableToLoad', 'Unable to load segments.')) + '</div>'); }
            });
        }
        function closeBulk() {
            if (!$bulk) { return; }
            if (document.activeElement && $bulk[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $bulk.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas141-modal-open'); }
        }
        function renderBulk(customers, segments) {
            // Segment selector.
            if (!segments.length) {
                $bulkSelect.html('<option value="0">' + escapeHtml(label('VAS_141_NoTargetLists', 'No target lists are configured.')) + '</option>');
            } else {
                $bulkSelect.html('<option value="0">—</option>' + segments.map(function (s) {
                    return '<option value="' + Number(s.id) + '">' + escapeHtml(s.name) + '</option>';
                }).join(''));
            }
            // Customer checklist.
            if (!customers.length) {
                $bulkBody.html('<div class="vas141-state">' + escapeHtml(label('VAS_141_AllSegmented', 'All active customers are segmented.')) + '</div>');
                updateBulkSave();
                return;
            }
            var rows = customers.map(function (c) {
                var name = c.customerName || '';
                var meta = [c.ownerName, formatMoney(c.customerValue, bulkCurrency)].filter(function (p) { return p; }).join(' · ');
                return '<label class="vas141-chk">' +
                    '<input type="checkbox" class="vas141-cbx" value="' + Number(c.customerId) + '" checked>' +
                    '<span class="vas141-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                    '<span class="vas141-chk-main"><span class="vas141-row-title" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                        '<span class="vas141-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span></span>' +
                '</label>';
            }).join('');
            $bulkBody.html('<div class="vas141-sectitle">' + escapeHtml(label('VAS_141_TopUnsegmented', 'Top unsegmented by value')) + '</div>' + rows);
            updateBulkSave();
        }
        function selectedCustomerIds() {
            var ids = [];
            $bulk.find('.vas141-cbx:checked').each(function () { ids.push(Number($(this).val())); });
            return ids;
        }
        function updateBulkSave() {
            var ok = Number($bulkSelect.val() || 0) > 0 && selectedCustomerIds().length > 0;
            $bulkSave.prop('disabled', !ok);
        }
        function saveBulk() {
            var segId = Number($bulkSelect.val() || 0);
            var ids = selectedCustomerIds();
            if (segId <= 0) { $bulkError.text(label('VAS_141_SelectList', 'Select a target list.')).show(); return; }
            if (!ids.length) { return; }
            $bulkSave.prop('disabled', true);
            $bulkError.hide().text('');
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_141_CustomersBySegmentWidget/AssignSegment',
                type: 'POST', data: { C_MasterTargetList_ID: segId, customerIds: ids.join(',') },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $bulkError.text(data.error).show(); $bulkSave.prop('disabled', false); return; }
                    closeBulk();
                    loadSegments();  // refresh distribution + header
                },
                error: function () { $bulkError.text(label('VAS_141_AssignFailed', 'Could not assign.')).show(); $bulkSave.prop('disabled', false); }
            });
        }
        function createBulkDialog() {
            $bulk = $(
                '<div class="vas141-bulk" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_141_BulkTitle', 'Segment customers')) + '">' +
                    '<div class="vas141-scrim" data-bulk-close></div>' +
                    '<section class="vas141-bpanel">' +
                        '<header class="vas141-phead"><h2 class="vas141-ptitle">' + escapeHtml(label('VAS_141_BulkTitle', 'Segment customers')) + '</h2>' +
                            '<button type="button" class="vas141-close" data-bulk-close aria-label="' + escapeHtml(label('VAS_141_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas141-bbody">' +
                            '<label class="vas141-flabel">' + escapeHtml(label('VAS_141_AddToSegment', 'Add selected to segment')) + '</label>' +
                            '<select class="vas141-bselect"></select>' +
                            '<div class="vas141-checklist"></div>' +
                            '<div class="vas141-berror"></div>' +
                        '</div>' +
                        '<footer class="vas141-bfoot">' +
                            '<button type="button" class="vas141-btn vas141-btn-ghost" data-bulk-close>' + escapeHtml(label('VAS_141_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas141-btn vas141-btn-primary" data-bulk-save>' + icon('target') + escapeHtml(label('VAS_141_AddButton', 'Add to segment')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($bulk);
            $bulkBody = $bulk.find('.vas141-checklist');
            $bulkSelect = $bulk.find('.vas141-bselect');
            $bulkError = $bulk.find('.vas141-berror');
            $bulkSave = $bulk.find('[data-bulk-save]');
            $bulkError.hide();
            $bulk.on('click', '[data-bulk-close]', closeBulk);
            $bulk.on('change', '.vas141-bselect, .vas141-cbx', updateBulkSave);
            $bulk.on('click', '[data-bulk-save]', saveBulk);
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas141-card">' +
                    '<div class="vas141-head">' +
                        '<div class="vas141-head-l">' +
                            '<span class="vas141-iconwell">' + icon('layers') + '</span>' +
                            '<div class="vas141-head-txt"><div class="vas141-title">' + escapeHtml(label('VAS_141_CustomersBySegment', 'Customers by segment')) + '</div>' +
                                '<div class="vas141-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas141-seglink">' + escapeHtml(label('VAS_141_SegmentLink', 'Segment')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas141-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas141-sub');
            $body = $card.find('.vas141-body');

            $card.on('click', '.vas141-seglink', function () { openBulk(); });
            $card.on('click', '.vas141-seg-l', function () { openSegList(Number($(this).attr('data-id')), $(this).attr('data-name')); });
            $card.on('click', '.vas141-wpgbtn', function () { turnSegPage($(this).attr('data-segdir')); });
            $card.on('click', '.vas141-retry', function () { loadSegments(); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createListDialog();
            createBulkDialog();
            $(document).on('keydown.MPCvas141', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($bulk && $bulk.hasClass('is-open')) { closeBulk(); }
                else if ($list && $list.hasClass('is-open')) { closeSegList(); }
            });
            loadSegments();
        };

        this.refreshWidget = function () { loadSegments(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas141');
            if ($list) { $list.remove(); $list = null; }
            if ($bulk) { $bulk.remove(); $bulk = null; }
            $('body').removeClass('vas141-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_141_CustomersBySegmentWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_141_CustomersBySegmentWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };
    VAS.VAS_141_CustomersBySegmentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_141_CustomersBySegmentWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_141_CustomersBySegmentWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_141_CustomersBySegmentWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
