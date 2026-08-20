/**
 * VAS_140 Contracts Expiring Widget (Customers module dashboard)
 * Purpose - 3x1 glass widget: active contracts expiring in the next 90 days split
 *           into three urgency windows (<=30d red, 31-60d amber, 61-90d blue). Each
 *           card shows the distinct contract count, converted value at stake and a
 *           high-value count. Header shows "Next 90 days · N high-value" and a
 *           "High-value ->" link. Clicking a card opens that window's contract list;
 *           the header link opens all high-value contracts. High-value = converted
 *           value >= 1.5x the average across the whole 0-90 population.
 * Design  - contracts-expiring.html (attached) + Design Specs/dashboard-widgets.md.
 *           Glass surface, icon-well header, three tinted urgency columns; drill-down
 *           contract-list modal with the shared circular pager. Internal sizing in em
 *           against the widget-root clamp; borders/strokes in px. CSS vas140-*.
 *
 * Backend - VAS_140_ContractsExpiringWidget/GetBuckets                (three windows + totals)
 *           VAS_140_ContractsExpiringWidget/GetContracts (bucket)     (paged drill-down list)
 *
 * Routing - 2026-08-17: hosted on a window, opening the customer navigates the host
 *           grid in place (widgetFirevalueChanged); on the Home / landing dashboard
 *           (windowNo < 0) there is no host grid, so the record is opened in the
 *           standard Customer window via VAS.ZoomUtil.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                       | Message Key
 * ----+------------------------------------+--------------------------------
 *  1  | Contracts expiring                 | VAS_140_ContractsExpiring
 *  2  | Next 90 days                       | VAS_140_Next90Days
 *  3  | high-value                         | VAS_140_HighValue
 *  4  | High-value                         | VAS_140_HighValueLink
 *  5  | Expiring                           | VAS_140_Expiring
 *  6  | value                              | VAS_140_Value
 *  7  | ≤30d                               | VAS_140_Le30
 *  8  | 31–60d                             | VAS_140_D3160
 *  9  | 61–90d                             | VAS_140_D6190
 * 10  | of                                 | VAS_140_Of
 * 10a | Showing                            | VAS_140_Showing
 * 10b | Previous page                      | VAS_140_PrevPage
 * 10c | Next page                          | VAS_140_NextPage
 * 11  | Nothing here right now.            | VAS_140_NothingHere
 * 12  | No contracts.                      | VAS_140_NoContracts
 * 13  | Unable to load contracts.          | VAS_140_UnableToLoad
 * 14  | Retry                              | VAS_140_Retry
 * 15  | Close                              | VAS_140_Close
 * 16  | Contracts expiring                 | VAS_140_ListTitle (prefix)
 * 17  | High-value contracts               | VAS_140_HighValueTitle
 * 18  | Next 90 days                       | VAS_140_All90Title
 * 19  | Automatic                          | VAS_140_RenewalA
 * 20  | Manual                             | VAS_140_RenewalM
 * 21  | No owner                           | VAS_140_NoOwner
 * 22  | expires in                         | VAS_140_ExpiresIn
 * 23  | d                                  | VAS_140_Days
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
        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };
        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_140_ContractsExpiringWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas140-root">');
        var $sub;
        var $band;

        var widgetCurrency = { symbol: '', iso: '', precision: 2 };

        // Drill-down modal state.
        var $list, $listBody, $listPager, $listTitle;
        var listBucket = 'ALL_90', listOffset = 0, listTotal = 0, listSeq = 0;
        var listCurrency = { symbol: '', iso: '', precision: 2 };
        var LIST_PAGE = 7;

        // AD_Window_ID of the Customer window, resolved once on the first Home-page
        // zoom and reused afterwards (0 = not resolved yet).
        var zoomWindowId = 0;

        // Urgency windows (red -> amber -> blue).
        var WINDOWS = [
            { key: 'LE_30', labelKey: 'VAS_140_Le30', labelText: '≤30d', color: '#ED1C24' },
            { key: 'D31_60', labelKey: 'VAS_140_D3160', labelText: '31–60d', color: '#D78B10' },
            { key: 'D61_90', labelKey: 'VAS_140_D6190', labelText: '61–90d', color: '#0083DA' }
        ];

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
        function renewalLabel(code) {
            // VAS Contract Master uses ATC/MNL; core C_Contract used A/M.
            if (code === 'ATC' || code === 'A') { return label('VAS_140_RenewalA', 'Automatic'); }
            if (code === 'MNL' || code === 'M') { return label('VAS_140_RenewalM', 'Manual'); }
            return code || '';
        }
        function urgencyColor(days) {
            var d = Number(days || 0);
            if (d <= 30) { return '#ED1C24'; }
            if (d <= 60) { return '#D78B10'; }
            return '#0083DA';
        }

        function icon(name) {
            if (name === 'doc') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><path d="M14 2v6h6"></path></svg>';
            }
            if (name === 'star') {
                return '<svg viewBox="0 0 24 24" fill="currentColor" stroke="currentColor" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>';
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

        /* ---------- Cards ---------- */

        function loadBuckets() {
            renderCardsState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_140_ContractsExpiringWidget/GetBuckets',
                type: 'GET', cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { renderCardsError(data.error); return; }
                    widgetCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
                    var hv90 = Number(data.highValueCount90 || 0);
                    $sub.text(label('VAS_140_Next90Days', 'Next 90 days') + ' · ' + formatCount(hv90) + ' ' + label('VAS_140_HighValue', 'high-value'));
                    renderCards(data.buckets || []);
                },
                error: function () { renderCardsError(); }
            });
        }

        function renderCardsState(message) {
            $band.html('<div class="vas140-state">' + escapeHtml(message) + '</div>');
        }
        function renderCardsError(detail) {
            var extra = detail ? '<div class="vas140-errdetail">' + escapeHtml(String(detail)) + '</div>' : '';
            $band.html('<div class="vas140-state">' + escapeHtml(label('VAS_140_UnableToLoad', 'Unable to load contracts.')) +
                ' <button type="button" class="vas140-retry">' + escapeHtml(label('VAS_140_Retry', 'Retry')) + '</button>' + extra + '</div>');
        }

        function cardHtml(win, bucket) {
            var count = Number((bucket && bucket.contractCount) || 0);
            var value = Number((bucket && bucket.valueAtStake) || 0);
            var hv = Number((bucket && bucket.highValueCount) || 0);
            var winLabel = label(win.labelKey, win.labelText);
            var valLine = formatMoney(value, widgetCurrency) + ' ' + label('VAS_140_Value', 'value') +
                (hv > 0 ? ' · ' + formatCount(hv) + ' ' + label('VAS_140_HighValue', 'high-value') : '');
            return '<button type="button" class="vas140-col" data-bucket="' + win.key + '" data-label="' + escapeHtml(winLabel) + '" ' +
                'style="background:' + win.color + '0d;border-color:' + win.color + '33">' +
                '<div class="vas140-col-top">' +
                    '<div class="vas140-col-win">' + escapeHtml(label('VAS_140_Expiring', 'Expiring')) + ' ' + escapeHtml(winLabel) + '</div>' +
                    '<div class="vas140-col-n" style="color:' + win.color + '">' + formatCount(count) + '</div>' +
                '</div>' +
                '<div class="vas140-col-val" title="' + escapeHtml(valLine) + '">' + escapeHtml(valLine) + '</div>' +
            '</button>';
        }

        function renderCards(buckets) {
            var byKey = {};
            for (var i = 0; i < buckets.length; i++) { byKey[buckets[i].key] = buckets[i]; }
            $band.html(WINDOWS.map(function (w) { return cardHtml(w, byKey[w.key]); }).join(''));
        }

        /* ---------- Drill-down contract list modal ---------- */

        function listTitleFor(bucket, cardLabel) {
            if (bucket === 'HIGH_VALUE') { return label('VAS_140_HighValueTitle', 'High-value contracts'); }
            if (bucket === 'ALL_90') { return label('VAS_140_ContractsExpiring', 'Contracts expiring') + ' · ' + label('VAS_140_All90Title', 'Next 90 days'); }
            return label('VAS_140_ContractsExpiring', 'Contracts expiring') + ' ' + (cardLabel || '');
        }

        function openList(bucket, cardLabel) {
            listBucket = bucket;
            listOffset = 0;
            $listTitle.text(listTitleFor(bucket, cardLabel));
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas140-modal-open');
            loadList();
        }
        function closeList() {
            if (!$list) { return; }
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            $('body').removeClass('vas140-modal-open');
        }
        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas140-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_140_ContractsExpiringWidget/GetContracts',
                type: 'GET', cache: false,
                data: { bucket: listBucket, offset: listOffset, limit: LIST_PAGE },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { $listBody.html('<div class="vas140-state">' + escapeHtml(label('VAS_140_UnableToLoad', 'Unable to load contracts.')) + '</div>'); return; }
                    listTotal = Number(data.total || 0);
                    listCurrency = { symbol: data.currency_symbol || widgetCurrency.symbol, iso: data.currency_iso || widgetCurrency.iso, precision: data.std_precision };
                    renderList(data.items || []);
                },
                error: function () { if (seq === listSeq) { $listBody.html('<div class="vas140-state">' + escapeHtml(label('VAS_140_UnableToLoad', 'Unable to load contracts.')) + '</div>'); } }
            });
        }

        function contractRowHtml(item) {
            var days = Number(item.daysToExpiry || 0);
            var color = urgencyColor(days);
            var hvStar = item.isHighValue ? '<span class="vas140-hv" title="' + escapeHtml(label('VAS_140_HighValue', 'high-value')) + '">' + icon('star') + '</span>' : '';
            var metaParts = [item.contractNumber, renewalLabel(item.renewalTypeCode), item.salesRepName || label('VAS_140_NoOwner', 'No owner')]
                .filter(function (p) { return p; });
            var meta = metaParts.join(' · ');
            return '<div class="vas140-row" data-cid="' + Number(item.customerId) + '">' +
                '<span class="vas140-row-ic" style="color:' + color + ';background:' + color + '1f">' + icon('doc') + '</span>' +
                '<span class="vas140-row-main">' +
                    '<span class="vas140-row-title" title="' + escapeHtml(item.customerName) + '">' + escapeHtml(item.customerName) + hvStar + '</span>' +
                    '<span class="vas140-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas140-row-right">' +
                    '<span class="vas140-row-val">' + escapeHtml(formatMoney(item.contractValue, listCurrency)) + '</span>' +
                    '<span class="vas140-row-days" style="color:' + color + ';background:' + color + '14">' + formatCount(days) + escapeHtml(label('VAS_140_Days', 'd')) + '</span>' +
                '</span>' +
            '</div>';
        }

        function renderList(items) {
            if (!items.length) {
                $listBody.html('<div class="vas140-state">' + escapeHtml(label('VAS_140_NoContracts', 'No contracts.')) + '</div>');
                $listPager.empty();
                return;
            }
            $listBody.html('<div class="vas140-list">' + items.map(contractRowHtml).join('') + '</div>');
            var start = listOffset + 1, end = listOffset + items.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            /* Footer pager (dashboard-widgets.md §"Widget Footer Pager"): helper
               left, compact prev · "N of M" · next right. The control stays put
               on a single page with both arrows disabled. */
            var of = label('VAS_140_Of', 'of');
            var helper = label('VAS_140_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listTotal);
            $listPager.html(
                '<span class="vas140-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas140-pgctl">' +
                    '<button type="button" class="vas140-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_140_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas140-pgtext">' + escapeHtml((current + 1) + ' ' + of + ' ' + pages) + '</span>' +
                    '<button type="button" class="vas140-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_140_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
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
                '<div class="vas140-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas140-scrim" data-list-close></div>' +
                    '<section class="vas140-panel">' +
                        '<header class="vas140-phead"><h2 class="vas140-ptitle"></h2>' +
                            '<button type="button" class="vas140-close" data-list-close aria-label="' + escapeHtml(label('VAS_140_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas140-pbody"></div>' +
                        '<footer class="vas140-pfoot"><div class="vas140-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas140-pbody');
            $listPager = $list.find('.vas140-pager');
            $listTitle = $list.find('.vas140-ptitle');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '.vas140-row', function () { zoomToCustomer(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas140-pgbtn', function () { turnPage($(this).attr('data-dir')); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas140-card">' +
                    '<div class="vas140-head">' +
                        '<div class="vas140-head-l">' +
                            '<span class="vas140-iconwell">' + icon('doc') + '</span>' +
                            '<div class="vas140-head-txt"><div class="vas140-title">' + escapeHtml(label('VAS_140_ContractsExpiring', 'Contracts expiring')) + '</div>' +
                                '<div class="vas140-sub"></div></div>' +
                        '</div>' +
                        '<button type="button" class="vas140-hvlink">' + escapeHtml(label('VAS_140_HighValueLink', 'High-value')) + ' ' + icon('chev') + '</button>' +
                    '</div>' +
                    '<div class="vas140-body"><div class="vas140-band"></div></div>' +
                '</div>'
            );
            $sub = $card.find('.vas140-sub');
            $band = $card.find('.vas140-band');

            $card.on('click', '.vas140-hvlink', function () { openList('HIGH_VALUE', ''); });
            $card.on('click', '.vas140-col', function () { openList($(this).attr('data-bucket'), $(this).attr('data-label')); });
            $card.on('keydown', '.vas140-col', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); openList($(this).attr('data-bucket'), $(this).attr('data-label')); }
            });
            $card.on('click', '.vas140-retry', function () { loadBuckets(); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createListDialog();
            $(document).on('keydown.MPCvas140', function (event) {
                if (event.key === 'Escape' && $list && $list.hasClass('is-open')) { closeList(); }
            });
            loadBuckets();
        };

        this.refreshWidget = function () { loadBuckets(); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas140');
            if ($list) { $list.remove(); $list = null; }
            $('body').removeClass('vas140-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_140_ContractsExpiringWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_140_ContractsExpiringWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };
    VAS.VAS_140_ContractsExpiringWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_140_ContractsExpiringWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_140_ContractsExpiringWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_140_ContractsExpiringWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
