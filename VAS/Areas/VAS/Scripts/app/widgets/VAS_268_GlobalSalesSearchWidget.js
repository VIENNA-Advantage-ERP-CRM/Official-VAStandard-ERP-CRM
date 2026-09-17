/**
 * VAS_268 Global Sales Search Widget (Sales Order dashboard, 9x1)
 * Purpose - the first widget on the Sales Order dashboard: a full-width omni
 *           search band. Empty focus shows "Recent across sales" (the 6 sales
 *           orders most recently updated by the logged-in user); typing (>=2
 *           chars, ~200ms debounce) searches nine field families in a fixed
 *           priority order (Sales order, Customer, Product, Representative,
 *           Warehouse, Quotation, Description, Order reference, Location) and
 *           renders up to 10 grouped rows, with a "View all N results" row
 *           when more match. Every result resolves to a concrete C_Order_ID.
 *           Clicking a row opens the shared Sales Order record-preview modal
 *           (built once here; a second widget on this dashboard that needs the
 *           same modal should lift this block into its own shared module -
 *           there is only one widget on this dashboard today, so this file
 *           owns it directly rather than speculatively splitting it out).
 * Design  - 01-global-search.html / .md: 80% centered glass search shell,
 *           overlay results dropdown (max-height, its own scroll - the
 *           no-inner-scroll widget rule does not apply to an overlay), group
 *           headers, category badges, keyboard nav, Esc hint. Modal: header
 *           stat tiles + a paginated line table that fits the available
 *           height (capped 10/page, minimum 2) so the modal body itself never
 *           scrolls.
 * Routing - Row click opens the preview modal only (no navigation). The
 *           modal's "Open Record" button navigates to the real Sales Order
 *           window (name VAS_SalesOrder) positioned on that exact C_Order_ID
 *           via the shared VAS.ZoomUtil helper (same mechanism VAS_120/078
 *           already use for cross-window record zoom - Prompt_Instructions
 *           "Scenario 2"). "View all N results" is best-effort: it opens the
 *           Sales Order window via the same ZoomUtil path without a record
 *           filter (ZoomUtil only supports a single-column EQUAL restriction,
 *           and reconstructing this widget's 9-category OR predicate as a
 *           client-side TabWhereClause risks the exact "duplicate document"
 *           grid-diagnostic bug already hit and fixed on VAS_181/182 earlier
 *           in this project) - the spec's own fallback clause explicitly
 *           allows this when the router cannot pre-apply the search text.
 *
 * Backend - VAS_268_GlobalSalesSearchWidget/SearchSalesOrders     (GET q,max -> rows + total)
 *           VAS_268_GlobalSalesSearchWidget/GetRecentSalesOrders  (GET -> rows)
 *           VAS_268_GlobalSalesSearchWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Search product, customer, location, representative, SO reference, description, warehouse… | VAS_268_SearchPlaceholder
 *  2  | Recent across sales                                                   | VAS_268_RecentAcrossSales
 *  3  | Searching…                                                            | VAS_268_Searching
 *  4  | Search is unavailable right now. Try again in a moment.               | VAS_268_SearchError
 *  5  | No sales records match "{q}". Try an SO number, customer, product, warehouse or representative. | VAS_268_NoMatches
 *  6  | View all {n} results                                                  | VAS_268_ViewAll
 *  7  | Close                                                                 | VAS_268_Close
 *  8  | Back                                                                  | VAS_268_Back
 *  9  | Open Record                                                           | VAS_268_OpenRecord
 * 10  | Customer                                                              | VAS_268_Customer
 * 11  | SO date                                                               | VAS_268_SoDate
 * 12  | Date promised                                                         | VAS_268_DatePromised
 * 13  | SO value                                                              | VAS_268_SoValue
 * 14  | Ship from                                                             | VAS_268_ShipFrom
 * 15  | Delivery mode                                                         | VAS_268_DeliveryMode
 * 16  | Document status                                                       | VAS_268_DocumentStatus
 * 17  | Delivery status                                                       | VAS_268_DeliveryStatus
 * 18  | Sales order lines                                                     | VAS_268_SalesOrderLines
 * 19  | Line                                                                  | VAS_268_ColLine
 * 20  | Product                                                               | VAS_268_ColProduct
 * 21  | Attribute                                                             | VAS_268_ColAttribute
 * 22  | UoM                                                                   | VAS_268_ColUom
 * 23  | Ordered                                                               | VAS_268_ColOrdered
 * 24  | Delivered                                                             | VAS_268_ColDelivered
 * 25  | Pending                                                               | VAS_268_ColPending
 * 26  | In stock                                                              | VAS_268_ColInStock
 * 27  | Rate                                                                  | VAS_268_ColRate
 * 28  | Amount                                                                | VAS_268_ColAmount
 * 29  | Line status                                                           | VAS_268_ColLineStatus
 * 30  | Drafted                                                               | VAS_268_LineDrafted
 * 31  | Voided                                                                | VAS_268_LineVoided
 * 32  | Delivered                                                             | VAS_268_LineDelivered
 * 33  | Partly delivered                                                      | VAS_268_LinePartial
 * 34  | In process                                                            | VAS_268_LineInProcess
 * 35  | Fully delivered                                                       | VAS_268_DeliveryFull
 * 36  | Partial                                                               | VAS_268_DeliveryPartial
 * 37  | Pending                                                               | VAS_268_DeliveryPending
 * 38  | Not applicable                                                        | VAS_268_DeliveryNA
 * 39  | lines                                                                 | VAS_268_LinesSuffix
 * 40  | qty ordered                                                           | VAS_268_QtyOrderedSuffix
 * 41  | qty short of stock                                                    | VAS_268_QtyShortSuffix
 * 42  | Previous page                                                         | VAS_268_PrevPage
 * 43  | Next page                                                             | VAS_268_NextPage
 * 44  | of                                                                    | VAS_268_Of
 * 45  | Showing                                                               | VAS_268_Showing
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var CATEGORY_ORDER = ['Sales order', 'Customer', 'Product', 'Representative', 'Warehouse', 'Quotation', 'Description', 'Order reference', 'Location'];
    var CATEGORY_TINT = {
        'Sales order': '#EAF8FF', 'Customer': '#E7F7EF', 'Product': '#FFF6E2', 'Representative': '#EFEEFF',
        'Warehouse': '#F1F4F8', 'Quotation': '#E7F7EF', 'Description': '#FCEFEF', 'Order reference': '#EAF8FF', 'Location': '#F1F4F8'
    };
    var MIN_QUERY_LENGTH = 2;
    var DEBOUNCE_MS = 200;
    var DROPDOWN_ROWS = 10;
    var MAX_LINES_PER_PAGE = 10;
    var MIN_LINES_PER_PAGE = 2;
    var ZOOM_WINDOW_NAME = 'VAS_SalesOrder';

    VAS.VAS_268_GlobalSalesSearchWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas268-root">');
        var $input, $results;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_268_GlobalSalesSearchWidget/';

        var searchTimer = null;
        var requestSequence = 0;
        var rows = [];
        var totalMatches = 0;
        var isRecentState = true;
        var activeIndex = -1;
        var lastQuery = '';

        var lineState = null; // { lines, page, size }
        var zoomWindowId = 0;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function icon(name) {
            if (name === 'search') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.2-3.2"/></svg>';
            }
            if (name === 'close') {
                return '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
            }
            if (name === 'arrow') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>';
            }
            if (name === 'prev') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
            }
            if (name === 'next') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';
            }
            return '';
        }

        // Indian-numbering display convention (₹ x.xx Cr / ₹ x.xx L / grouped
        // integer) - matches the design mock's fmtINR exactly (01-global-search.html).
        function formatINR(value) {
            var num = Number(value || 0);
            if (num >= 1e7) { return '₹ ' + (num / 1e7).toFixed(2) + ' Cr'; }
            if (num >= 1e5) { return '₹ ' + (num / 1e5).toFixed(2) + ' L'; }
            return '₹ ' + Math.round(num).toLocaleString('en-IN');
        }

        function formatNum(value) {
            return Math.round(Number(value || 0)).toLocaleString('en-IN');
        }

        var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
        function formatDateShort(iso) {
            if (!iso) { return ''; }
            var d = new Date(iso + 'T00:00:00');
            if (isNaN(d.getTime())) { return iso; }
            return (d.getDate() < 10 ? '0' : '') + d.getDate() + ' ' + MONTHS[d.getMonth()] + ' ' + d.getFullYear();
        }

        this.Initalize = function () {
            createWidget();
            createModal();
            bindEvents();
        };

        function createWidget() {
            var placeholder = label('VAS_268_SearchPlaceholder', 'Search product, customer, location, representative, SO reference, description, warehouse…');

            $root.html(
                '<div class="vas268-shell">' +
                    '<div class="vas268-box">' +
                        '<span class="vas268-icon">' + icon('search') + '</span>' +
                        '<input class="vas268-input" type="text" autocomplete="off" aria-label="' + escapeHtml(placeholder) + '" placeholder="' + escapeHtml(placeholder) + '">' +
                        '<span class="vas268-kbd">Esc</span>' +
                    '</div>' +
                    '<div class="vas268-results" role="listbox"></div>' +
                '</div>'
            );

            $input = $root.find('.vas268-input');
            $results = $root.find('.vas268-results');
        }

        function createModal() {
            // Lives on <body>, not inside $root, so the widget cell's own
            // overflow/stacking context cannot clip the overlay (same approach
            // VAS_120/078 already use for their suggestion popovers).
            $mask = $('<div class="vas268-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas268-modal"></div>');
            $mHead = $('<div class="vas268-mhead"></div>');
            var $htxt = $('<div class="vas268-htxt"></div>');
            $mBack = $('<button type="button" class="vas268-xbtn" aria-label="' + escapeHtml(label('VAS_268_Back', 'Back')) + '" hidden>' +
                '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg></button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas268-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas268-xbtn" aria-label="' + escapeHtml(label('VAS_268_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas268-mbody"></div>');
            $mFoot = $('<div class="vas268-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            $closeBtn.on('click', closeModal);
        }

        function bindEvents() {
            var ns = '.vas268-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $input.on('focus', function () {
                if ($input.val().trim().length < MIN_QUERY_LENGTH) { loadRecent(); }
                else { scheduleSearch(); }
            });
            $input.on('input', scheduleSearch);
            $input.on('keydown', handleKeydown);

            $results.on('mousedown', '.vas268-ritem', function (event) {
                event.preventDefault();
                selectRow(Number($(this).attr('data-index')));
            });
            $results.on('mousedown', '.vas268-rmore', function (event) {
                event.preventDefault();
                openSalesOrderList();
            });

            $(document).on('mousedown' + ns, function (event) {
                if (!$(event.target).closest('.vas268-shell, .vas268-modal').length) {
                    closeResults();
                }
            });
            $(document).on('keydown' + ns, function (event) {
                if (event.key !== 'Escape') { return; }
                closeResults();
            });
            $(window).on('resize' + ns, function () {
                if ($mask.hasClass('is-open')) { fitLineTable(); }
            });

            $mBody.on('click', function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnLinePage(Number(pageBtn.getAttribute('data-dir'))); }
            });
        }

        function scheduleSearch() {
            if (searchTimer) { clearTimeout(searchTimer); }

            var text = $input.val().trim();
            if (text.length < MIN_QUERY_LENGTH) {
                requestSequence += 1;
                if (text.length === 0) { loadRecent(); }
                else { closeResults(); }
                return;
            }

            searchTimer = setTimeout(function () { doSearch(text); }, DEBOUNCE_MS);
        }

        function loadRecent() {
            var sequence = ++requestSequence;
            isRecentState = true;
            lastQuery = '';
            renderState(label('VAS_268_Searching', 'Searching…'));

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRecentSalesOrders',
                type: 'GET', dataType: 'json', cache: false,
                success: function (res) {
                    if (sequence !== requestSequence) { return; }
                    var parsed = parseResponse(res);
                    if (parsed.Error) { renderState(label('VAS_268_SearchError', 'Search is unavailable right now. Try again in a moment.')); return; }
                    rows = (parsed.Rows || []).map(normalizeRow);
                    totalMatches = rows.length;
                    activeIndex = rows.length ? 0 : -1;
                    renderRows();
                },
                error: function () {
                    if (sequence !== requestSequence) { return; }
                    renderState(label('VAS_268_SearchError', 'Search is unavailable right now. Try again in a moment.'));
                }
            });
        }

        function doSearch(text) {
            var sequence = ++requestSequence;
            isRecentState = false;
            lastQuery = text;
            renderState(label('VAS_268_Searching', 'Searching…'));

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'SearchSalesOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { q: text, max: DROPDOWN_ROWS },
                success: function (res) {
                    // Stale-response guard: a newer keystroke already fired.
                    if (sequence !== requestSequence) { return; }
                    var parsed = parseResponse(res);
                    if (parsed.Error) { renderState(label('VAS_268_SearchError', 'Search is unavailable right now. Try again in a moment.')); return; }
                    rows = (parsed.Rows || []).map(normalizeRow);
                    totalMatches = Number(parsed.Total || rows.length);
                    activeIndex = rows.length ? 0 : -1;
                    renderRows();
                },
                error: function () {
                    if (sequence !== requestSequence) { return; }
                    renderState(label('VAS_268_SearchError', 'Search is unavailable right now. Try again in a moment.'));
                }
            });
        }

        function normalizeRow(row) {
            return {
                Category: row.Category || '',
                MatchedText: row.MatchedText || '',
                SalesOrderId: Number(row.SalesOrderId) || 0,
                SalesOrderNumber: row.SalesOrderNumber || '',
                SalesOrderDate: row.SalesOrderDate || '',
                CustomerName: row.CustomerName || '',
                DocumentStatus: row.DocumentStatus || '',
                OrderValue: Number(row.OrderValue) || 0
            };
        }

        function renderState(message) {
            rows = [];
            activeIndex = -1;
            $results.html('<div class="vas268-rempty">' + escapeHtml(message) + '</div>').addClass('is-open');
        }

        function renderRows() {
            if (!rows.length) {
                if (isRecentState) {
                    $results.html(
                        '<div class="vas268-rgroup">' + escapeHtml(label('VAS_268_RecentAcrossSales', 'Recent across sales')) + '</div>' +
                        '<div class="vas268-rempty">' + escapeHtml(label('VAS_268_NoRecent', 'No recent sales orders.')) + '</div>'
                    ).addClass('is-open');
                } else {
                    var noMatchMsg = label('VAS_268_NoMatches', 'No sales records match "{q}". Try an SO number, customer, product, warehouse or representative.').replace('{q}', lastQuery);
                    $results.html('<div class="vas268-rempty">' + escapeHtml(noMatchMsg) + '</div>').addClass('is-open');
                }
                return;
            }

            var html = '';
            var lastGroup = null;

            if (isRecentState) {
                html += '<div class="vas268-rgroup">' + escapeHtml(label('VAS_268_RecentAcrossSales', 'Recent across sales')) + '</div>';
            }

            rows.forEach(function (row, index) {
                if (!isRecentState && row.Category !== lastGroup) {
                    lastGroup = row.Category;
                    html += '<div class="vas268-rgroup">' + escapeHtml(row.Category) + '</div>';
                }

                var secondary = [row.SalesOrderNumber, formatDateShort(row.SalesOrderDate), row.CustomerName, row.DocumentStatus]
                    .filter(function (part) { return part; }).join(' · ');
                var primary = isRecentState ? row.SalesOrderNumber : row.MatchedText;
                var tint = CATEGORY_TINT[row.Category] || '#F1F4F8';
                var badgeLetter = isRecentState ? 'S' : (row.Category.charAt(0) || 'S');

                html += '<button type="button" class="vas268-ritem' + (index === activeIndex ? ' is-active' : '') + '" role="option" data-index="' + index + '">' +
                    '<span class="vas268-ico" style="background:' + tint + '">' + escapeHtml(badgeLetter) + '</span>' +
                    '<span class="vas268-txt">' +
                        '<span class="vas268-t1" title="' + escapeHtml(primary) + '">' + escapeHtml(primary) + '</span>' +
                        '<span class="vas268-t2" title="' + escapeHtml(secondary) + '">' + escapeHtml(secondary) + '</span>' +
                    '</span>' +
                    '<span class="vas268-t3">' + escapeHtml(formatINR(row.OrderValue)) + '</span>' +
                '</button>';
            });

            if (!isRecentState && totalMatches > rows.length) {
                var moreText = label('VAS_268_ViewAll', 'View all {n} results').replace('{n}', totalMatches);
                html += '<div class="vas268-rmore" role="button" tabindex="0">' + escapeHtml(moreText) + ' ' + icon('arrow') + '</div>';
            }

            $results.html(html).addClass('is-open');
        }

        function closeResults() {
            if (!$results) { return; }
            $results.removeClass('is-open').empty();
            activeIndex = -1;
        }

        function handleKeydown(event) {
            if (event.key === 'Enter') {
                if (activeIndex >= 0 && rows[activeIndex]) {
                    event.preventDefault();
                    selectRow(activeIndex);
                } else if (!isRecentState && totalMatches > 0) {
                    event.preventDefault();
                    openSalesOrderList();
                }
                return;
            }
            if (event.key === 'Escape') { closeResults(); return; }
            if (!$results.hasClass('is-open') || !rows.length) { return; }

            if (event.key === 'ArrowDown') {
                event.preventDefault();
                activeIndex = (activeIndex + 1) % rows.length;
                renderRows();
            } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                activeIndex = activeIndex <= 0 ? rows.length - 1 : activeIndex - 1;
                renderRows();
            }
        }

        function selectRow(index) {
            var row = rows[index];
            if (!row) { return; }
            closeResults();
            openOrderModal(row.SalesOrderId);
        }

        // Best-effort: opens the Sales Order window without pre-applying the typed
        // search text (see the file header note on why a TabWhereClause
        // reconstruction of the 9-category predicate is deliberately avoided).
        function openSalesOrderList() {
            closeResults();
            if (!window.VAS || !VAS.ZoomUtil) { return; }
            VAS.ZoomUtil.zoomToRecord('C_Order_ID', 0, zoomWindowId, ZOOM_WINDOW_NAME, ZOOM_WINDOW_NAME)
                .done(function (id) { if (id > 0) { zoomWindowId = id; } });
        }

        /* ============================================================
         * Shared Sales Order record-preview modal
         * ============================================================ */
        function openOrderModal(orderId) {
            if (!orderId) { return; }
            $mBack.prop('hidden', true); // this widget never nests a modal-from-modal
            $mTitle.text(label('VAS_268_Searching', 'Searching…'));
            $mSub.text('');
            $mBody.html('<div class="vas268-mstate">' + escapeHtml(label('VAS_268_Searching', 'Searching…')) + '</div>');
            $mFoot.html('');
            $mask.addClass('is-open');

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetSalesOrderDetail',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: orderId },
                success: function (res) {
                    var parsed = parseResponse(res);
                    var order = parsed && parsed.Order;
                    if (parsed.Error || !order || !order.SalesOrderId) {
                        $mBody.html('<div class="vas268-mstate">' + escapeHtml(label('VAS_268_SearchError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
                        return;
                    }
                    renderOrderModal(order, parsed.Lines || []);
                },
                error: function () {
                    $mBody.html('<div class="vas268-mstate">' + escapeHtml(label('VAS_268_SearchError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
                }
            });
        }

        function statTile(l, v) {
            return '<div class="vas268-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        function deliveryChipClass(text) {
            var t = String(text || '').toLowerCase();
            if (t.indexOf('full') >= 0) { return 'vas268-chip-ok'; }
            if (t.indexOf('partial') >= 0) { return 'vas268-chip-warn'; }
            if (t.indexOf('not applicable') >= 0) { return 'vas268-chip-neutral'; }
            return 'vas268-chip-neutral';
        }

        function lineStatus(order, line) {
            if (order.DocumentStatusCode === 'DR') { return { text: label('VAS_268_LineDrafted', 'Drafted'), cls: 'vas268-chip-neutral' }; }
            if (order.DocumentStatusCode === 'VO') { return { text: label('VAS_268_LineVoided', 'Voided'), cls: 'vas268-chip-risk' }; }
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_268_LineDelivered', 'Delivered'), cls: 'vas268-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_268_LinePartial', 'Partly delivered'), cls: 'vas268-chip-warn' }; }
            return { text: label('VAS_268_LineInProcess', 'In process'), cls: 'vas268-chip-prop' };
        }

        function renderOrderModal(order, lines) {
            $mTitle.text(order.SalesOrderNumber);
            $mSub.text([order.CustomerName, formatDateShort(order.SalesOrderDate), order.DocumentStatus].filter(function (p) { return p; }).join(' · '));

            var statsHtml = '<div class="vas268-mstats">' +
                statTile(label('VAS_268_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_268_SoDate', 'SO date'), formatDateShort(order.SalesOrderDate)) +
                statTile(label('VAS_268_DatePromised', 'Date promised'), formatDateShort(order.DatePromised)) +
                statTile(label('VAS_268_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_268_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_268_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_268_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_268_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';

            var secHtml = '<div class="vas268-msec">' + escapeHtml(label('VAS_268_SalesOrderLines', 'Sales order lines')) + '</div>';

            $mBody.html(statsHtml + secHtml + '<div class="vas268-mtwrap"><div class="vas268-mtbl" id="vas268-linetbl"></div></div>');

            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });

            lineState = { order: order, lines: lines, page: 0, size: MAX_LINES_PER_PAGE };
            drawLineTable();

            var footNote = lines.length + ' ' + label('VAS_268_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_268_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_268_QtyShortSuffix', 'qty short of stock') : '');

            $mFoot.html(
                '<span class="vas268-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span>' +
                    '<button type="button" class="vas268-btn" id="vas268-mclose">' + escapeHtml(label('VAS_268_Close', 'Close')) + '</button> ' +
                    '<button type="button" class="vas268-btn vas268-btn-primary" id="vas268-mopen">' + escapeHtml(label('VAS_268_OpenRecord', 'Open Record')) + '</button>' +
                '</span>'
            );
            $mFoot.find('#vas268-mclose').on('click', closeModal);
            $mFoot.find('#vas268-mopen').on('click', function () { openRecordInWindow(order.SalesOrderId); });

            requestAnimationFrame(function () { fitLineTable(); requestAnimationFrame(fitLineTable); });
        }

        function drawLineTable() {
            var el = document.getElementById('vas268-linetbl');
            if (!el || !lineState) { return; }

            var cols = [
                { label: label('VAS_268_ColLine', 'Line'), w: .35, align: 'right' },
                { label: label('VAS_268_ColProduct', 'Product'), w: 1.5 },
                { label: label('VAS_268_ColAttribute', 'Attribute'), w: 1.1 },
                { label: label('VAS_268_ColUom', 'UoM'), w: .5 },
                { label: label('VAS_268_ColOrdered', 'Ordered'), w: .7, align: 'right' },
                { label: label('VAS_268_ColDelivered', 'Delivered'), w: .7, align: 'right' },
                { label: label('VAS_268_ColPending', 'Pending'), w: .7, align: 'right' },
                { label: label('VAS_268_ColInStock', 'In stock'), w: .7, align: 'right' },
                { label: label('VAS_268_ColRate', 'Rate'), w: .7, align: 'right' },
                { label: label('VAS_268_ColAmount', 'Amount'), w: .9, align: 'right' },
                { label: label('VAS_268_ColLineStatus', 'Line status'), w: 1 }
            ];
            var tpl = cols.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');

            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas268-mrow vas268-mhead-row" style="grid-template-columns:' + tpl + '">' +
                cols.map(function (c) { return '<span class="vas268-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(lineState.order, line);
                var cells = [
                    { v: String(line.LineNo), cls: 'vas268-c-std', align: 'right' },
                    { v: line.ProductName, cls: 'vas268-c-prim' },
                    { v: line.AttributeText, cls: 'vas268-c-std' },
                    { v: line.UomName, cls: 'vas268-c-std' },
                    { v: formatNum(line.QtyOrdered), cls: 'vas268-c-std', align: 'right' },
                    { v: formatNum(line.QtyDelivered), cls: 'vas268-c-std', align: 'right' },
                    { v: formatNum(line.QtyPending), cls: 'vas268-c-prim', align: 'right' },
                    { v: formatNum(line.FreeStock), cls: (line.FreeStock < line.QtyPending ? 'vas268-c-short' : 'vas268-c-ok'), align: 'right' },
                    { v: '₹ ' + formatNum(line.Rate), cls: 'vas268-c-std', align: 'right' },
                    { v: formatINR(line.Amount), cls: 'vas268-c-emph', align: 'right' }
                ];
                var rowHtml = cells.map(function (c, ci) {
                    return '<span class="vas268-cell' + (cols[ci].align === 'right' ? ' right' : '') + ' ' + c.cls + '" title="' + escapeHtml(c.v) + '">' + escapeHtml(c.v) + '</span>';
                }).join('') + '<span class="vas268-cell"><span class="vas268-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas268-mrow" style="grid-template-columns:' + tpl + '">' + rowHtml + '</div>';
            }).join('');

            var showingLabel = label('VAS_268_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' + label('VAS_268_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas268-mtfoot"><span class="vas268-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas268-pager">' +
                        '<button type="button" class="vas268-pbtn" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_268_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas268-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_268_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas268-pbtn" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_268_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas268-mbody-rows">' + body + '</div>' + foot;
        }

        function turnLinePage(dir) {
            if (!lineState) { return; }
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            lineState.page = Math.min(pages - 1, Math.max(0, lineState.page + dir));
            drawLineTable();
        }

        // Sizes the line table's rows-per-page to the space actually left in the
        // modal body, so the body never grows an inner scrollbar (spec §7 / §9).
        function fitLineTable() {
            var el = document.getElementById('vas268-linetbl');
            if (!el || !lineState) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas268-mhead-row');
            var foot = el.querySelector('.vas268-mtfoot');
            var row = el.querySelector('.vas268-mbody-rows .vas268-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_LINES_PER_PAGE, Math.min(MAX_LINES_PER_PAGE, n));
            if (n !== lineState.size) { lineState.size = n; drawLineTable(); }
        }

        function closeModal() {
            $mask.removeClass('is-open');
            lineState = null;
        }

        // "Open Record" - navigates to the real Sales Order window positioned on
        // this exact record, via the shared cross-window zoom helper
        // (Prompt_Instructions "Scenario 2: Open Another Screen from Widget").
        function openRecordInWindow(orderId) {
            closeModal();
            if (!window.VAS || !VAS.ZoomUtil) { return; }
            VAS.ZoomUtil.zoomToRecord('C_Order_ID', orderId, zoomWindowId, ZOOM_WINDOW_NAME, ZOOM_WINDOW_NAME)
                .done(function (id) { if (id > 0) { zoomWindowId = id; } });
        }

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas268-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { /* stateless search widget - nothing to refresh */ };
    };

    VAS.VAS_268_GlobalSalesSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_268_GlobalSalesSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_268_GlobalSalesSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_268_GlobalSalesSearchWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_268_GlobalSalesSearchWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
