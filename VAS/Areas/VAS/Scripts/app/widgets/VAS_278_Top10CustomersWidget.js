/**
 * VAS_278 Top 10 Customers Widget (Sales Order dashboard, 3x2 ranked bar list)
 * Purpose - The ten individual C_BPartner customers with the highest booked Sales
 *           Order value for a selected Month/Year, ranked descending, bar width
 *           relative to rank 1. Booked = same cohort as VAS_273/277 - active real
 *           Sales Order, IsSOTrx='Y', non-quotation, non-return, DocStatus 'CO' or
 *           'CL'. Five rows visible per widget page; the widget fetches all ten rows
 *           once per period and pages between them client-side without refetching.
 *           Period change re-fetches the ranking and resets the widget page to 1.
 *
 * Design  - 11-top-10-customers.html / .md: glass 3x2 tile, header with title +
 *           subtitle + a Month/Year period filter (both selects call
 *           stopPropagation so they never also trigger a row's click handler), an
 *           `.hlist` of baseline-aligned name/value rows each with a bar `.track`
 *           tinted by row index (blue/teal/amber/lilac/rose cycling every 5 rows).
 *           Row click opens the customer's drill-down: a 4-card stat strip (SO
 *           value, Open SOs, Pending delivery, On-time %) and that customer's
 *           booked Sales Orders for the period in the standard column set
 *           (icon/SO No/SO date/Customer/Warehouse/Representative/Value/Delivery/
 *           Status). SO number and the lines icon open the same shared record/lines
 *           child modals as every other widget on this dashboard.
 *
 * Backend - VAS_278_Top10CustomersWidget/GetTopCustomers       (GET month,year -> ranked top 10)
 *           VAS_278_Top10CustomersWidget/GetCustomerSummary    (GET customerId,month,year -> 4-card stat strip)
 *           VAS_278_Top10CustomersWidget/GetCustomerOrders     (GET customerId,month,year,page,size -> paginated documents)
 *           VAS_278_Top10CustomersWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Top 10 Customers                                                      | VAS_278_Title
 *  2  | By SO value                                                           | VAS_278_Subtitle
 *  3  | ranked by SO value                                                    | VAS_278_RankedBy
 *  4  | No orders booked in                                                   | VAS_278_ZeroStatePrefix
 *  5  | Ranking unavailable                                                   | VAS_278_ErrorState
 *  6  | Customer sales orders ·                                               | VAS_278_SubtitlePrefix
 *  7  | SO value                                                              | VAS_278_StatOrderValue
 *  8  | Open SOs                                                              | VAS_278_StatOpenOrders
 *  9  | Pending delivery                                                      | VAS_278_StatPendingDelivery
 * 10  | On-time %                                                             | VAS_278_StatOnTime
 * 11  | Sales orders                                                          | VAS_278_SectionHeading
 * 12  | click a row to open the record                                        | VAS_278_ClickRowHint
 * 13  | SO No                                                                 | VAS_278_ColSoNo
 * 14  | SO date                                                               | VAS_278_SoDate
 * 15  | Customer                                                              | VAS_278_Customer
 * 16  | Warehouse                                                             | VAS_278_ColWarehouse
 * 17  | Representative                                                        | VAS_278_Representative
 * 18  | Value                                                                 | VAS_278_ColValue
 * 19  | Delivery                                                              | VAS_278_ColDelivery
 * 20  | Status                                                                | VAS_278_ColStatus
 * 21  | Back                                                                  | VAS_278_Back
 * 22  | Close                                                                 | VAS_278_Close
 * 23  | Date promised                                                         | VAS_278_DatePromised
 * 24  | SO value                                                              | VAS_278_SoValue
 * 25  | Ship from                                                             | VAS_278_ShipFrom
 * 26  | Delivery mode                                                         | VAS_278_DeliveryMode
 * 27  | Document status                                                       | VAS_278_DocumentStatus
 * 28  | Delivery status                                                       | VAS_278_DeliveryStatus
 * 29  | Sales order lines                                                     | VAS_278_SalesOrderLines
 * 30  | Line                                                                  | VAS_278_ColLine
 * 31  | Product                                                               | VAS_278_ColProduct
 * 32  | Attribute                                                             | VAS_278_ColAttribute
 * 33  | UoM                                                                   | VAS_278_ColUom
 * 34  | Ordered                                                               | VAS_278_ColOrdered
 * 35  | Delivered                                                             | VAS_278_ColDelivered
 * 36  | Pending                                                               | VAS_278_ColPending
 * 37  | In stock                                                              | VAS_278_ColInStock
 * 38  | Rate                                                                  | VAS_278_ColRate
 * 39  | Amount                                                                | VAS_278_ColAmount
 * 40  | Line status                                                           | VAS_278_ColLineStatus
 * 41  | Fully delivered                                                       | VAS_278_DeliveryFull
 * 42  | Partial                                                               | VAS_278_DeliveryPartial
 * 43  | Pending                                                               | VAS_278_DeliveryPending
 * 44  | Partly delivered                                                      | VAS_278_LinePartial
 * 45  | In process                                                            | VAS_278_LineInProcess
 * 46  | Sales order                                                           | VAS_278_SalesOrderPrefix
 * 47  | lines                                                                 | VAS_278_LinesSuffix
 * 48  | qty ordered                                                           | VAS_278_QtyOrderedSuffix
 * 49  | qty short of stock                                                    | VAS_278_QtyShortSuffix
 * 50  | Previous page                                                         | VAS_278_PrevPage
 * 51  | Next page                                                             | VAS_278_NextPage
 * 52  | of                                                                    | VAS_278_Of
 * 53  | Showing                                                               | VAS_278_Showing
 * 54  | Search is unavailable right now. Try again in a moment.               | VAS_278_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var WIDGET_PAGE_SIZE = 5;   // rows visible per widget page (client-side, no refetch)
    var TOP_COUNT = 10;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MIN_YEAR = 2024;
    var YEAR_SPAN = 3; // MIN_YEAR .. MIN_YEAR + YEAR_SPAN - 1, covers "current + 1"

    // 5-color cycle by row index, matching the reference bar palette.
    var BAR_COLORS = ['#A9D2FF', '#A3E0D4', '#FFDCA1', '#CFC9F5', '#FFC7C7'];

    var now = new Date();
    var CUR_M = now.getMonth();
    var CUR_Y = now.getFullYear();

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-277). */
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

    VAS.VAS_278_Top10CustomersWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas278-root">');
        var $shell, $list, $helper, $pageTxt, $prevBtn, $nextBtn, $monthSel, $yearSel;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_278_Top10CustomersWidget/';

        var rankState = 'loading'; // 'loading' | 'ready' | 'error'
        var customers = [];        // all 10 rows for the current period
        var widgetPage = 0;

        var custState = null;      // { customerId, customerName, summary, docs:{page,size,total,rows} }
        var lineState = null;      // { order, lines, page, size, tableId }
        var cfgStack = [];
        var currentCfg = null;

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

        function icon(name) {
            if (name === 'close') {
                return '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
            }
            if (name === 'back') {
                return '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>';
            }
            if (name === 'prev') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
            }
            if (name === 'next') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';
            }
            if (name === 'lines') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>';
            }
            return '';
        }

        function formatINR(value) {
            var num = Number(value || 0);
            if (num >= 1e7) { return '₹ ' + (num / 1e7).toFixed(2) + ' Cr'; }
            if (num >= 1e5) { return '₹ ' + (num / 1e5).toFixed(2) + ' L'; }
            return '₹ ' + Math.round(num).toLocaleString('en-IN');
        }

        function formatNum(value) {
            return Math.round(Number(value || 0)).toLocaleString('en-IN');
        }

        function formatPercent(value) {
            if (value === null || value === undefined) { return '—'; }
            var num = Number(value);
            return (Math.round(num * 10) / 10) + '%';
        }

        function parseIso(iso) {
            if (!iso) { return null; }
            var d = new Date(iso + 'T00:00:00');
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDateShort(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return (d.getDate() < 10 ? '0' : '') + d.getDate() + ' ' + MONTHS[d.getMonth()];
        }

        function formatDateFull(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return (d.getDate() < 10 ? '0' : '') + d.getDate() + ' ' + MONTHS[d.getMonth()] + ' ' + d.getFullYear();
        }

        function periodLabel() {
            return MONTHS[selectedMonth()] + ' ' + selectedYear();
        }

        function selectedMonth() { return $monthSel && $monthSel.length ? Number($monthSel.val()) : CUR_M; }
        function selectedYear() { return $yearSel && $yearSel.length ? Number($yearSel.val()) : CUR_Y; }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas278-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas278-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (title/subtitle + Month/Year filter) + bar list
         * ============================================================ */
        function fillMonthSelect($sel) {
            $sel.html(MONTHS.map(function (m, i) {
                return '<option value="' + i + '"' + (i === CUR_M ? ' selected' : '') + '>' + m + '</option>';
            }).join(''));
        }

        function fillYearSelect($sel) {
            var html = '';
            for (var y = MIN_YEAR; y < MIN_YEAR + YEAR_SPAN; y++) {
                html += '<option value="' + y + '"' + (y === CUR_Y ? ' selected' : '') + '>' + y + '</option>';
            }
            $sel.html(html);
        }

        function createWidget() {
            $shell = $('<div class="vas278-shell"></div>');

            var $head = $('<div class="vas278-head"></div>');
            var $htxt = $('<div class="vas278-head-txt"></div>');
            $htxt.append('<p class="vas278-title">' + escapeHtml(label('VAS_278_Title', 'Top 10 Customers')) + '</p>');
            $htxt.append('<p class="vas278-sub">' + escapeHtml(label('VAS_278_Subtitle', 'By SO value')) + '</p>');

            var $filter = $('<div class="vas278-mfilter"></div>');
            $monthSel = $('<select class="vas278-msel" aria-label="Month"></select>');
            $yearSel = $('<select class="vas278-msel" aria-label="Year"></select>');
            fillMonthSelect($monthSel);
            fillYearSelect($yearSel);
            $filter.append($monthSel, $yearSel);

            $head.append($htxt, $filter);

            $list = $('<div class="vas278-hlist"></div>');

            var $foot = $('<div class="vas278-wfoot"></div>');
            $helper = $('<span class="vas278-helper"></span>');
            var $pager = $('<div class="vas278-pager"></div>');
            $prevBtn = $('<button type="button" class="vas278-pbtn" aria-label="' + escapeHtml(label('VAS_278_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>');
            $pageTxt = $('<span class="vas278-ptxt"></span>');
            $nextBtn = $('<button type="button" class="vas278-pbtn" aria-label="' + escapeHtml(label('VAS_278_NextPage', 'Next page')) + '">' + icon('next') + '</button>');
            $pager.append($prevBtn, $pageTxt, $nextBtn);
            $foot.append($helper, $pager);

            $shell.append($head, $list, $foot);
            $root.append($shell);

            $monthSel.on('click', function (event) { event.stopPropagation(); });
            $yearSel.on('click', function (event) { event.stopPropagation(); });
            $monthSel.on('change', function () { loadTopCustomers(); });
            $yearSel.on('change', function () { loadTopCustomers(); });

            $prevBtn.on('click', function () { turnWidgetPage(-1); });
            $nextBtn.on('click', function () { turnWidgetPage(1); });

            $list.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-cid]') : null;
                if (row) { openCustomerModal(Number(row.getAttribute('data-cid')), row.getAttribute('data-cname')); }
            });
        }

        function loadTopCustomers() {
            rankState = 'loading';
            widgetPage = 0;
            renderList();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetTopCustomers',
                type: 'GET', dataType: 'json', cache: false,
                data: { month: selectedMonth(), year: selectedYear() },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Customers) { rankState = 'error'; customers = []; }
                    else { rankState = 'ready'; customers = parsed.Customers; }
                    renderList();
                },
                error: function () {
                    rankState = 'error'; customers = [];
                    renderList();
                }
            });
        }

        function skeletonHtml() {
            var rows = '';
            for (var i = 0; i < WIDGET_PAGE_SIZE; i++) {
                rows += '<div class="vas278-hrow vas278-skel-row">' +
                    '<span class="vas278-line"><span class="vas278-skel-nm"></span></span>' +
                    '<span class="vas278-track"><span class="vas278-fill"></span></span></div>';
            }
            return rows;
        }

        function renderList() {
            if (rankState === 'loading') {
                $list.html(skeletonHtml());
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }
            if (rankState === 'error') {
                $list.html('<div class="vas278-empty">' + escapeHtml(label('VAS_278_ErrorState', 'Ranking unavailable')) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }
            if (customers.length <= 0) {
                $list.html('<div class="vas278-empty">' + escapeHtml(label('VAS_278_ZeroStatePrefix', 'No orders booked in') + ' ' + periodLabel()) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            var pages = Math.max(1, Math.ceil(customers.length / WIDGET_PAGE_SIZE));
            if (widgetPage > pages - 1) { widgetPage = pages - 1; }

            var topValue = customers.length ? Number(customers[0].OrderValue || 0) : 0;
            var start = widgetPage * WIDGET_PAGE_SIZE;
            var slice = customers.slice(start, start + WIDGET_PAGE_SIZE);

            // A full page of rows fills the cell edge-to-edge (space-between, per
            // design); a sparse/partial page stacks from the top instead of
            // stretching just 1-4 rows to the two extreme edges of the cell.
            $list.toggleClass('vas278-hlist-top', slice.length < WIDGET_PAGE_SIZE);

            $list.html(slice.map(function (c, i) {
                var pct = topValue > 0 ? Math.max(2, Math.round((Number(c.OrderValue || 0) / topValue) * 100)) : 0;
                var color = BAR_COLORS[(start + i) % BAR_COLORS.length];
                return '<button type="button" class="vas278-hrow" data-cid="' + c.CustomerId + '" data-cname="' + escapeHtml(c.CustomerName) + '">' +
                    '<span class="vas278-line"><span class="vas278-nm" title="' + escapeHtml(c.CustomerName) + '">' + escapeHtml(c.CustomerName) + '</span>' +
                    '<span class="vas278-vl" title="' + escapeHtml(formatINR(c.OrderValue)) + '">' + escapeHtml(formatINR(c.OrderValue)) + '</span></span>' +
                    '<span class="vas278-track"><span class="vas278-fill" style="width:' + pct + '%;background:' + color + '"></span></span></button>';
            }).join(''));

            var helperText = label('VAS_278_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' + label('VAS_278_Of', 'of') + ' ' + customers.length + ' · ' + label('VAS_278_RankedBy', 'ranked by SO value');
            $helper.attr('title', helperText).text(helperText);
            $pageTxt.text((widgetPage + 1) + ' ' + label('VAS_278_Of', 'of') + ' ' + pages);
            $prevBtn.prop('disabled', widgetPage === 0);
            $nextBtn.prop('disabled', widgetPage >= pages - 1);
        }

        function turnWidgetPage(dir) {
            var pages = Math.max(1, Math.ceil(customers.length / WIDGET_PAGE_SIZE));
            var next = Math.min(pages - 1, Math.max(0, widgetPage + dir));
            if (next === widgetPage) { return; }
            widgetPage = next;
            renderList();
        }

        /* ============================================================
         * Modal shell - lives on <body>, not inside $root, so the widget
         * cell's own overflow cannot clip it. Every screen is a
         * { title, subtitle, size, render } config; showScreen() pushes the
         * outgoing config onto cfgStack and paints the new one; backModal()
         * pops and re-renders the popped config from its own live state
         * (custState / lineState) - never a frozen HTML snapshot.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas278-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas278-modal"></div>');
            $mHead = $('<div class="vas278-mhead"></div>');
            var $htxt = $('<div class="vas278-htxt"></div>');
            $mBack = $('<button type="button" class="vas278-xbtn" aria-label="' + escapeHtml(label('VAS_278_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas278-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas278-xbtn" aria-label="' + escapeHtml(label('VAS_278_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas278-mbody"></div>');
            $mFoot = $('<div class="vas278-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            $closeBtn.on('click', closeModal);
            $mBack.on('click', backModal);
            $mBody.on('click', function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnPage(pageBtn.getAttribute('data-table'), Number(pageBtn.getAttribute('data-dir'))); return; }
                var soBtn = event.target.closest ? event.target.closest('[data-so]') : null;
                if (soBtn) { openRecordModal(Number(soBtn.getAttribute('data-so'))); return; }
                var linesBtn = event.target.closest ? event.target.closest('[data-lines]') : null;
                if (linesBtn) { openLinesModal(Number(linesBtn.getAttribute('data-lines'))); return; }
            });
        }

        function bindDocumentLevelEvents() {
            var ns = '.vas278-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $(window).on('resize' + ns, function () {
                if ($mask.hasClass('is-open')) { fitAllTables(); }
            });
        }

        function paintChrome(cfg) {
            $mBack.prop('hidden', !cfgStack.length);
            $modal.removeClass('sm md').addClass(cfg.size || '');
            $mTitle.text(cfg.title || '');
            $mSub.text(cfg.subtitle || '');
        }

        function showScreen(cfg, push) {
            if (push && currentCfg) { cfgStack.push(currentCfg); }
            else if (!push) { cfgStack = []; }
            currentCfg = cfg;
            paintChrome(cfg);
            cfg.render();
            $mask.addClass('is-open');
            requestAnimationFrame(function () { fitAllTables(); requestAnimationFrame(fitAllTables); });
        }

        function backModal() {
            var prev = cfgStack.pop();
            if (!prev) { closeModal(); return; }
            currentCfg = prev;
            paintChrome(prev);
            prev.render();
            requestAnimationFrame(function () { fitAllTables(); requestAnimationFrame(fitAllTables); });
        }

        function closeModal() {
            $mask.removeClass('is-open');
            cfgStack = [];
            currentCfg = null;
            custState = null;
            lineState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || '');
            $mSub.text('');
            $mBody.html('<div class="vas278-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas278-mstate">' + escapeHtml(label('VAS_278_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Customer drill-down (top-level modal opened from a row click)
         * ============================================================ */
        function openCustomerModal(customerId, customerName) {
            showLoading(customerName);

            fetchCustomerSummary(customerId, function (summaryOk, summary) {
                if (!summaryOk) { showLoadError(); return; }
                fetchCustomerOrders(customerId, 0, MAX_ROWS_PER_PAGE, function (ordersOk, total, rows) {
                    if (!ordersOk) { showLoadError(); return; }
                    custState = {
                        customerId: customerId,
                        customerName: customerName,
                        summary: summary,
                        docs: { page: 0, size: MAX_ROWS_PER_PAGE, total: total, rows: rows }
                    };
                    showScreen(buildCustomerCfg(customerName), false);
                });
            });
        }

        function buildCustomerCfg(customerName) {
            return {
                title: customerName,
                subtitle: label('VAS_278_SubtitlePrefix', 'Customer sales orders') + ' · ' + periodLabel(),
                size: '',
                render: renderCustomerBody
            };
        }

        function fetchCustomerSummary(customerId, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetCustomerSummary',
                type: 'GET', dataType: 'json', cache: false,
                data: { customerId: customerId, month: selectedMonth(), year: selectedYear() },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { cb(false); return; }
                    cb(true, parsed);
                },
                error: function () { cb(false); }
            });
        }

        function fetchCustomerOrders(customerId, page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetCustomerOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { customerId: customerId, month: selectedMonth(), year: selectedYear(), page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { cb(false); return; }
                    cb(true, Number(parsed.Total || 0), (parsed.Rows || []).map(normalizeOrderRow));
                },
                error: function () { cb(false); }
            });
        }

        function normalizeOrderRow(row) {
            return {
                SalesOrderId: Number(row.SalesOrderId) || 0,
                SalesOrderNumber: row.SalesOrderNumber || '',
                SalesOrderDate: row.SalesOrderDate || '',
                CustomerName: row.CustomerName || '',
                WarehouseName: row.WarehouseName || '',
                RepresentativeName: row.RepresentativeName || '',
                OrderValue: Number(row.OrderValue) || 0,
                DocumentStatus: row.DocumentStatus || '',
                DeliveryStatus: row.DeliveryStatus || '',
                DeliveryStatusCode: row.DeliveryStatusCode || 'PENDING'
            };
        }

        function deliveryChipClass(code) {
            if (code === 'FULL') { return 'vas278-chip-ok'; }
            if (code === 'PARTIAL') { return 'vas278-chip-warn'; }
            return 'vas278-chip-neutral';
        }

        function renderCustomerBody() {
            var summary = custState.summary;
            var statsHtml = '<div class="vas278-mstats">' +
                statTile(label('VAS_278_StatOrderValue', 'SO value'), formatINR(summary.OrderValue)) +
                statTile(label('VAS_278_StatOpenOrders', 'Open SOs'), formatNum(summary.OpenOrders)) +
                statTile(label('VAS_278_StatPendingDelivery', 'Pending delivery'), formatNum(summary.PendingDelivery)) +
                statTile(label('VAS_278_StatOnTime', 'On-time %'), formatPercent(summary.OnTimePercent)) +
            '</div>';

            var secHtml = '<div class="vas278-msec">' + escapeHtml(label('VAS_278_SectionHeading', 'Sales orders')) + '</div>';

            $mBody.html(statsHtml + secHtml + '<div class="vas278-mtwrap"><div class="vas278-mtbl" id="vas278-docstbl"></div></div>');
            drawDocumentsTable();

            $mFoot.html('<span class="vas278-foot-note"></span><span><button type="button" class="vas278-btn" id="vas278-mclose">' + escapeHtml(label('VAS_278_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas278-mclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_278_ColSoNo', 'SO No'), w: 1.2 },
            { key: 'date', label: label('VAS_278_SoDate', 'SO date'), w: 1 },
            { key: 'customer', label: label('VAS_278_Customer', 'Customer'), w: 1.7 },
            { key: 'wh', label: label('VAS_278_ColWarehouse', 'Warehouse'), w: 1.2 },
            { key: 'rep', label: label('VAS_278_Representative', 'Representative'), w: 1.2 },
            { key: 'value', label: label('VAS_278_ColValue', 'Value'), w: .9, align: 'right' },
            { key: 'delivery', label: label('VAS_278_ColDelivery', 'Delivery'), w: 1.05 },
            { key: 'status', label: label('VAS_278_ColStatus', 'Status'), w: 1.1 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas278-docstbl');
            if (!el || !custState) { return; }
            var docs = custState.docs;
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docs.total / docs.size));

            var head = '<div class="vas278-mrow vas278-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas278-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docs.rows.map(function (row) {
                var cells = [
                    '<span class="vas278-cell center"><button type="button" class="vas278-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas278-cell"><button type="button" class="vas278-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas278-c-std'),
                    cellHtml(row.CustomerName, 'vas278-c-prim'),
                    cellHtml(row.WarehouseName || '—', 'vas278-c-std'),
                    cellHtml(row.RepresentativeName || '—', 'vas278-c-std'),
                    cellHtml(formatINR(row.OrderValue), 'vas278-c-emph', 'right'),
                    '<span class="vas278-cell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas278-chip ' + deliveryChipClass(row.DeliveryStatusCode) + '">' + escapeHtml(row.DeliveryStatus) + '</span></span>',
                    '<span class="vas278-cell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas278-chip vas278-chip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas278-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docs.page * docs.size;
            var showingLabel = label('VAS_278_Showing', 'Showing') + ' ' + (docs.total ? (start + 1) : 0) + '–' + (start + docs.rows.length) + ' ' + label('VAS_278_Of', 'of') + ' ' + docs.total + ' · ' + label('VAS_278_ClickRowHint', 'click a row to open the record');

            var foot = '<div class="vas278-mtfoot"><span class="vas278-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas278-pager">' +
                        '<button type="button" class="vas278-pbtn" data-table="docs" data-dir="-1"' + (docs.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_278_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas278-ptxt">' + (docs.page + 1) + ' ' + escapeHtml(label('VAS_278_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas278-pbtn" data-table="docs" data-dir="1"' + (docs.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_278_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas278-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs' && custState) {
                var docs = custState.docs;
                var pages = Math.max(1, Math.ceil(docs.total / docs.size));
                var next = Math.min(pages - 1, Math.max(0, docs.page + dir));
                if (next === docs.page) { return; }
                fetchCustomerOrders(custState.customerId, next, docs.size, function (ok, total, rows) {
                    if (!ok) { return; }
                    docs.page = next; docs.total = total; docs.rows = rows;
                    drawDocumentsTable();
                });
                return;
            }
            if (table === 'lines' && lineState) {
                var linePages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
                lineState.page = Math.min(linePages - 1, Math.max(0, lineState.page + dir));
                drawLineTable(lineState.tableId);
            }
        }

        /* ============================================================
         * Record / lines child modals - both render from one
         * GetSalesOrderDetail fetch (order header + full line list).
         * ============================================================ */
        function lineStatus(line) {
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_278_DeliveryFull', 'Fully delivered'), cls: 'vas278-chip-ok' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_278_LinePartial', 'Partly delivered'), cls: 'vas278-chip-warn' }; }
            return { text: label('VAS_278_LineInProcess', 'In process'), cls: 'vas278-chip-prop' };
        }

        function fetchOrderDetail(orderId, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetSalesOrderDetail',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: orderId },
                success: function (res) {
                    var parsed = parseResponse(res);
                    var order = parsed && parsed.Order;
                    if (parsed.Error || !order || !order.SalesOrderId) { cb(null); return; }
                    cb({ order: order, lines: parsed.Lines || [] });
                },
                error: function () { cb(null); }
            });
        }

        function openRecordModal(orderId) {
            showLoading((custState ? custState.customerName : label('VAS_278_Title', 'Top 10 Customers')) + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildRecordCfg(detail.order, detail.lines), true);
            });
        }

        function buildRecordCfg(order, lines) {
            return {
                title: order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DocumentStatus].filter(function (p) { return p; }).join(' · '),
                size: '',
                render: function () { renderRecordBody(order, lines); }
            };
        }

        function soHeaderStats(order) {
            return '<div class="vas278-mstats">' +
                statTile(label('VAS_278_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_278_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_278_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_278_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_278_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_278_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_278_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_278_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas278-msec">' + escapeHtml(label('VAS_278_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas278-mtwrap"><div class="vas278-mtbl" id="vas278-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas278-linetbl-record' };
            drawLineTable('vas278-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas278-mback').on('click', backModal);
            $mFoot.find('#vas278-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_278_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_278_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_278_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas278-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas278-btn" id="vas278-mback">' + escapeHtml(label('VAS_278_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas278-btn" id="vas278-mclose">' + escapeHtml(label('VAS_278_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_278_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_278_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas278-polink">' + escapeHtml(label('VAS_278_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas278-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas278-mstats">' +
                statTile(label('VAS_278_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_278_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_278_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_278_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas278-msec">' + escapeHtml(label('VAS_278_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas278-mtwrap"><div class="vas278-mtbl" id="vas278-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas278-linetbl-lines' };
            drawLineTable('vas278-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas278-mback').on('click', backModal);
            $mFoot.find('#vas278-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_278_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_278_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_278_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_278_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_278_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_278_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_278_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_278_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_278_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_278_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_278_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas278-mrow vas278-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas278-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas278-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas278-c-prim'),
                    cellHtml(line.AttributeText, 'vas278-c-std'),
                    cellHtml(line.UomName, 'vas278-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas278-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas278-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas278-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas278-c-short' : 'vas278-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas278-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas278-c-emph', 'right')
                ].join('') + '<span class="vas278-cell"><span class="vas278-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas278-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_278_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_278_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas278-mtfoot"><span class="vas278-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas278-pager">' +
                        '<button type="button" class="vas278-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_278_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas278-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_278_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas278-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_278_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas278-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. */
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas278-mhead-row');
            var foot = el.querySelector('.vas278-mtfoot');
            var row = el.querySelector('.vas278-mbody-rows .vas278-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            if (custState) {
                fitTable('vas278-docstbl', custState.docs.size, function (n) {
                    var docs = custState.docs;
                    var newPages = Math.max(1, Math.ceil(docs.total / n));
                    var page = Math.min(docs.page, newPages - 1);
                    fetchCustomerOrders(custState.customerId, page, n, function (ok, total, rows) {
                        if (!ok) { return; }
                        docs.page = page; docs.size = n; docs.total = total; docs.rows = rows;
                        drawDocumentsTable();
                    });
                });
            }
            if (lineState) {
                fitTable(lineState.tableId, lineState.size, function (n) {
                    lineState.size = n;
                    drawLineTable(lineState.tableId);
                });
            }
        }

        this.Initalize = function () {
            createWidget();
            createModal();
            bindDocumentLevelEvents();
            loadTopCustomers();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas278-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadTopCustomers(); };
    };

    VAS.VAS_278_Top10CustomersWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_278_Top10CustomersWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_278_Top10CustomersWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_278_Top10CustomersWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_278_Top10CustomersWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
