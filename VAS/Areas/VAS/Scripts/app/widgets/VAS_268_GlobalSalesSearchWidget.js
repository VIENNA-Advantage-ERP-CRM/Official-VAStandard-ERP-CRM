/**
 * VAS_268 Global Sales Search Widget (Sales Order dashboard, 9x1)
 * Purpose - the first widget on the Sales Order dashboard: a full-width omni
 *           search band. Nothing is listed until the user types (Sr 7.1 - the
 *           old "Recent across sales" list on focus is retired); an (x) button
 *           clears the search (Sr 7.3); typing (>=2
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
 *           modal's "Open Record" button opens that exact C_Order_ID in the
 *           SAME tab (Sr 7.2): hosted in the Sales Order window it fires the
 *           host's TabWhereClause channel (VAS_078/203 pattern); on the Home
 *           page it falls back to VAS.ZoomUtil. "View all N results" (Sr 7.4)
 *           re-runs the search without the 10-row cap and lists every match
 *           in the scrollable dropdown instead of opening the landing page.
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
 * 46  | Esc                                                                   | VAS_268_EscHint
 * 47  | Warehouse                                                             | VAS_268_Warehouse
 * 48  | Clear                                                                 | VAS_268_Clear
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
    var VIEW_ALL_ROWS = 500;   // server-side cap for "View all" (MaxViewAllRows)
    var MAX_LINES_PER_PAGE = 10;
    var MIN_LINES_PER_PAGE = 2;
    var ZOOM_WINDOW_NAME = 'VAS_SalesOrder';

    VAS.VAS_268_GlobalSalesSearchWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas268-root">');
        var $input, $clear, $results;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_268_GlobalSalesSearchWidget/';
        // Per-instance suffix for the event namespace (Sr 8.28): when the dashboard
        // re-creates a widget, disposing the OLD instance must not unbind the
        // document/window handlers the NEW instance (same AD_UserHomeWidgetID) bound.
        var instanceTag = 'i' + Math.random().toString(36).slice(2, 9);

        var searchTimer = null;
        var requestSequence = 0;
        var rows = [];
        var totalMatches = 0;
        var isRecentState = false;   // "recent" list retired (Sr 7.1) - kept false
        var activeIndex = -1;
        var lastQuery = '';

        var lineState = null; // { lines, page, size }
        var zoomWindowId = 0;

        function label(key, fallback) {
            return VIS.Msg.getMsg(key);
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

        // Sr 8.7: amounts in the order's (price list's) own currency, with that
        // currency's precision - not a hard-coded rupee sign.
        function formatMoney(value, symbol, precision) {
            var prec = (typeof precision === 'number' && precision >= 0) ? precision : 2;
            var text = Number(value || 0).toLocaleString('en-IN', { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return symbol ? symbol + ' ' + text : text;
        }

        // Sr 7.6: quantities are in the line's selected UOM and can be fractional.
        function formatQty(value) {
            return Number(value || 0).toLocaleString('en-IN', { maximumFractionDigits: 2 });
        }

        // An attribute set instance with no values can still carry a description made
        // of blanks or separators only (e.g. "-") - that is not an attribute value
        // (same rule as VAS_203, Sr 8.2).
        function hasAttributeValue(text) {
            return !!text && /[^\s\-‐-―_.,:;()\[\]\/\\|]/.test(String(text));
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
                        '<button type="button" class="vas268-clear" tabindex="-1" hidden aria-label="' + escapeHtml(label('VAS_268_Clear', 'Clear')) + '" title="' + escapeHtml(label('VAS_268_Clear', 'Clear')) + '">' + icon('close') + '</button>' +
                        '<span class="vas268-kbd">' + escapeHtml(label('VAS_268_EscHint', 'Esc')) + '</span>' +
                    '</div>' +
                    '<div class="vas268-results" role="listbox"></div>' +
                '</div>'
            );

            $input = $root.find('.vas268-input');
            $clear = $root.find('.vas268-clear');
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

        /* Every handler is bound under one namespace and unbound first, so this can
           run again at any time (Sr 8.28: refreshWidget re-runs it - if the host
           strips jQuery handlers while re-laying out the mosaic, the search box and
           the modal would otherwise stay dead until the page is reloaded). */
        function bindEvents() {
            var ns = '.vas268-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;

            $input.off(ns);
            $clear.off(ns);
            $results.off(ns);
            $mBody.off(ns);
            $(document).off(ns);
            $(window).off(ns);

            // Sr 7.1: nothing is listed on focus - results appear only once the user
            // has typed a search; focusing again just re-opens an existing result list.
            $input.on('focus' + ns, function () {
                if ($input.val().trim().length >= MIN_QUERY_LENGTH && rows.length) { renderRows(); }
            });
            $input.on('input' + ns, function () { toggleClear(); scheduleSearch(); });
            $input.on('keydown' + ns, handleKeydown);

            // Sr 7.3: clear the entered search.
            $clear.on('mousedown' + ns, function (event) { event.preventDefault(); });
            $clear.on('click' + ns, function () {
                clearSearch();
                $input.focus();
            });

            $results.on('mousedown' + ns, '.vas268-ritem', function (event) {
                event.preventDefault();
                selectRow(Number($(this).attr('data-index')));
            });
            $results.on('mousedown' + ns, '.vas268-rmore', function (event) {
                event.preventDefault();
                viewAllResults();
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

            $mBody.on('click' + ns, function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnLinePage(Number(pageBtn.getAttribute('data-dir'))); }
            });
        }

        function toggleClear() {
            $clear.prop('hidden', !$input.val().length);
        }

        function clearSearch() {
            if (searchTimer) { clearTimeout(searchTimer); searchTimer = null; }
            requestSequence += 1;          // drop any in-flight response
            $input.val('');
            lastQuery = '';
            rows = [];
            totalMatches = 0;
            toggleClear();
            closeResults();
        }

        function scheduleSearch() {
            if (searchTimer) { clearTimeout(searchTimer); }

            var text = $input.val().trim();
            if (text.length < MIN_QUERY_LENGTH) {
                // Sr 7.1: an empty / too-short box shows nothing (no default orders).
                requestSequence += 1;
                rows = [];
                closeResults();
                return;
            }

            searchTimer = setTimeout(function () { doSearch(text, false); }, DEBOUNCE_MS);
        }

        // Sr 7.4: "View all N results" lists every match for the typed text right
        // here in the (scrollable) results dropdown instead of leaving the dashboard.
        function viewAllResults() {
            if (!lastQuery) { return; }
            doSearch(lastQuery, true);
        }

        function doSearch(text, all) {
            var sequence = ++requestSequence;
            isRecentState = false;
            lastQuery = text;
            renderState(label('VAS_268_Searching', 'Searching…'));

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'SearchSalesOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { q: text, max: all ? VIEW_ALL_ROWS : DROPDOWN_ROWS },
                success: function (res) {
                    // Stale-response guard: a newer keystroke already fired.
                    if (sequence !== requestSequence) { return; }
                    var parsed = parseResponse(res);
                    if (parsed.Error) { renderState(label('VAS_268_SearchError', 'Search is unavailable right now. Try again in a moment.')); return; }
                    rows = (parsed.Rows || []).map(normalizeRow);
                    totalMatches = Number(parsed.Total || rows.length);
                    activeIndex = rows.length ? 0 : -1;
                    renderRows();
                    if (all) { $results.scrollTop(0); }
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
                OrderValue: Number(row.OrderValue) || 0,
                CurrencySymbol: row.CurrencySymbol || ''
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
                    '<span class="vas268-t3">' + escapeHtml(formatMoney(row.OrderValue, row.CurrencySymbol)) + '</span>' +
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
                } else if (!isRecentState && totalMatches > rows.length) {
                    event.preventDefault();
                    viewAllResults();
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

            // Sr 8.8: Customer / SO date / Delivery mode / Document status cards removed
            // (already in the subtitle). Sr 7.9: "Ship from" is now "Warehouse".
            var statsHtml = '<div class="vas268-mstats">' +
                statTile(label('VAS_268_DatePromised', 'Date promised'), formatDateShort(order.DatePromised)) +
                statTile(label('VAS_268_SoValue', 'SO value'), formatMoney(order.OrderValue, order.CurrencySymbol, order.CurrencyPrecision)) +
                statTile(label('VAS_268_Warehouse', 'Warehouse'), order.WarehouseName) +
                statTile(label('VAS_268_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';

            var secHtml = '<div class="vas268-msec">' + escapeHtml(label('VAS_268_SalesOrderLines', 'Sales order lines')) + '</div>';

            $mBody.html(statsHtml + secHtml + '<div class="vas268-mtwrap"><div class="vas268-mtbl" id="vas268-linetbl"></div></div>');

            // Ordered / shortage totals count Item-type product lines only (Sr 7.11 /
            // 8.1). Shortage = pending - on hand per line (Sr 8.3), never negative.
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                if (!line.IsItem) { return; }
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var onHand = Math.max(0, Number(line.FreeStock) || 0);
                if (pending > onHand) { qtyShort += (pending - onHand); }
            });

            var showAttribute = lines.some(function (line) { return hasAttributeValue(line.AttributeText); });
            lineState = { order: order, lines: lines, page: 0, size: MAX_LINES_PER_PAGE, showAttribute: showAttribute };
            drawLineTable();

            var footNote = lines.length + ' ' + label('VAS_268_LinesSuffix', 'lines') +
                ' · ' + formatQty(qtyOrdered) + ' ' + label('VAS_268_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatQty(qtyShort) + ' ' + label('VAS_268_QtyShortSuffix', 'qty short of stock') : '');

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

            // Sr 8.2: the Attribute column is drawn only when at least one line of the
            // order carries an attribute value (decided over ALL lines, not the page).
            var showAttribute = !!lineState.showAttribute;
            var cols = [
                { key: 'line', label: label('VAS_268_ColLine', 'Line'), w: .35, align: 'right' },
                { key: 'product', label: label('VAS_268_ColProduct', 'Product'), w: 1.5 },
                { key: 'attr', label: label('VAS_268_ColAttribute', 'Attribute'), w: 1.1 },
                { key: 'uom', label: label('VAS_268_ColUom', 'UoM'), w: .5 },
                { key: 'ordered', label: label('VAS_268_ColOrdered', 'Ordered'), w: .7, align: 'right' },
                { key: 'delivered', label: label('VAS_268_ColDelivered', 'Delivered'), w: .7, align: 'right' },
                { key: 'pending', label: label('VAS_268_ColPending', 'Pending'), w: .7, align: 'right' },
                { key: 'instock', label: label('VAS_268_ColInStock', 'In stock'), w: .7, align: 'right' },
                { key: 'rate', label: label('VAS_268_ColRate', 'Rate'), w: .8, align: 'right' },
                { key: 'amount', label: label('VAS_268_ColAmount', 'Amount'), w: .9, align: 'right' },
                { key: 'status', label: label('VAS_268_ColLineStatus', 'Line status'), w: 1 }
            ].filter(function (c) { return showAttribute || c.key !== 'attr'; });
            var cur = lineState.order.CurrencySymbol;
            var prec = lineState.order.CurrencyPrecision;
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
                // Delivered / pending / in-stock are shown for Item-type products only
                // (Sr 7.11 / 8.1); charges and other product types show a dash.
                var isItem = !!line.IsItem;
                var cells = [
                    { v: String(line.LineNo), cls: 'vas268-c-std', align: 'right' },
                    { v: line.ProductName, cls: 'vas268-c-prim' }
                ];
                if (showAttribute) { cells.push({ v: hasAttributeValue(line.AttributeText) ? line.AttributeText : '', cls: 'vas268-c-std' }); }
                cells = cells.concat([
                    { v: line.UomName, cls: 'vas268-c-std' },
                    { v: formatQty(line.QtyOrdered), cls: 'vas268-c-std', align: 'right' },
                    { v: isItem ? formatQty(line.QtyDelivered) : '—', cls: 'vas268-c-std', align: 'right' },
                    { v: isItem ? formatQty(line.QtyPending) : '—', cls: 'vas268-c-prim', align: 'right' },
                    { v: isItem ? formatQty(line.FreeStock) : '—', cls: (isItem && line.FreeStock < line.QtyPending ? 'vas268-c-short' : 'vas268-c-ok'), align: 'right' },
                    { v: formatMoney(line.Rate, cur, 2), cls: 'vas268-c-std', align: 'right' },
                    { v: formatMoney(line.Amount, cur, prec), cls: 'vas268-c-emph', align: 'right' }
                ]);
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

        // "Open Record" (Sr 7.2) - navigates to the Sales Order record in the SAME tab.
        // Hosted inside the Sales Order window (windowNo >= 0) the host's value-changed
        // channel re-queries the window it is already in (TabWhereClause) - nothing new
        // is opened (same pattern as VAS_078 / VAS_203). Only on the Home page, where
        // there is no host window, the shared cross-window zoom helper is used.
        function openRecordInWindow(orderId) {
            closeModal();
            if (!orderId) { return; }
            if ($self.windowNo >= 0) {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_Order.C_Order_ID=" + Number(orderId),
                    "TabLayout": "Y",
                    "TabIndex": "0"
                });
                return;
            }
            if (!window.VAS || !VAS.ZoomUtil) { return; }
            VAS.ZoomUtil.zoomToRecord('C_Order_ID', orderId, zoomWindowId, ZOOM_WINDOW_NAME, ZOOM_WINDOW_NAME)
                .done(function (id) { if (id > 0) { zoomWindowId = id; } });
        }

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas268-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        // Stateless search widget - a refresh only re-binds the handlers (Sr 8.28) and
        // re-attaches the modal shell if the host removed it from <body>.
        this.refreshWidget = function () {
            if (!$mask || !$.contains(document.body, $mask[0])) { createModal(); }
            bindEvents();
        };
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
