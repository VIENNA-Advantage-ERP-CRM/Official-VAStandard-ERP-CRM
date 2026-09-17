/**
 * VAS_285 Top Products Widget (Sales Order dashboard, 2x2 compact ranked bar list)
 * Purpose - The four products driving the most booked Sales Order value for a
 *           selected Month/Year, ranked descending, bar width relative to rank 1
 *           (never to the widget width, and never a proportional-share bar - the
 *           reference bar list scales strictly against the top-ranked product so a
 *           month with one dominant SKU still shows the others at a readable width).
 *           Booked = same cohort as the category/customer value widgets - active
 *           real Sales Order, IsSOTrx='Y', non-quotation, non-return, DocStatus 'CO'
 *           or 'CL'; ranked by SUM(C_OrderLine.LineNetAmt) so this reconciles with
 *           the category donut for the same period. Exactly four rows, never more,
 *           never padded to four when fewer exist. No pager - four rows is the whole
 *           dataset.
 *
 *           USER OVERRIDE (18_Top_Products_Claude_Development_Prompt.txt): the paired
 *           HTML mock ships with no period control at all; this prompt explicitly
 *           adds the same compact Month/Year filter used by every other Sales
 *           dashboard value widget, defaulted to the current month, calling
 *           stopPropagation() so changing it never also opens a row's modal.
 *
 * Design  - 18-top-products.html / .md: glass 2x2 tile, header with title + subtitle
 *           + the added Month/Year filter, an `.hlist` of baseline-aligned
 *           name/value rows (bar tinted blue/teal/amber/lilac cycling by rank). Row
 *           click opens the product's demand detail: a 4-card stat strip (SO value
 *           for the period, qty on open SO, free stock, coverage %) and that
 *           product's booked Sales Orders for the period in the standard column set.
 *           SO number and the lines icon open the same shared record/lines child
 *           modals as every other widget on this dashboard. Per the prompt, "qty on
 *           open SO" and "free stock" are CURRENT operational figures (never period-
 *           filtered) so they agree with the Short Supply widget for the same product.
 *
 * Backend - VAS_285_TopProductsWidget/GetTopProducts       (GET month,year -> ranked top 4)
 *           VAS_285_TopProductsWidget/GetProductDemand     (GET productId,month,year -> 4-card stat strip)
 *           VAS_285_TopProductsWidget/GetProductOrders     (GET productId,month,year,page,size -> paginated documents)
 *           VAS_285_TopProductsWidget/GetSalesOrderDetail  (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Top Products                                                          | VAS_285_Title
 *  2  | By SO value                                                           | VAS_285_Subtitle
 *  3  | No product demand recorded                                           | VAS_285_ZeroState
 *  4  | Ranking unavailable                                                  | VAS_285_ErrorState
 *  5  | Sales orders carrying this product ·                                 | VAS_285_SubtitlePrefix
 *  6  | SO value                                                             | VAS_285_StatOrderValue
 *  7  | Qty on open SO                                                       | VAS_285_StatOpenQty
 *  8  | Free stock                                                           | VAS_285_StatFreeStock
 *  9  | Coverage                                                             | VAS_285_StatCoverage
 * 10  | Sales orders                                                         | VAS_285_SectionHeading
 * 11  | click a row to open the record                                       | VAS_285_ClickRowHint
 * 12  | SO No                                                                | VAS_285_ColSoNo
 * 13  | SO date                                                              | VAS_285_SoDate
 * 14  | Customer                                                             | VAS_285_Customer
 * 15  | Warehouse                                                            | VAS_285_ColWarehouse
 * 16  | Representative                                                       | VAS_285_Representative
 * 17  | Value                                                                | VAS_285_ColValue
 * 18  | Delivery                                                             | VAS_285_ColDelivery
 * 19  | Status                                                               | VAS_285_ColStatus
 * 20  | Back                                                                 | VAS_285_Back
 * 21  | Close                                                                | VAS_285_Close
 * 22  | Date promised                                                        | VAS_285_DatePromised
 * 23  | SO value                                                             | VAS_285_SoValue
 * 24  | Ship from                                                            | VAS_285_ShipFrom
 * 25  | Delivery mode                                                        | VAS_285_DeliveryMode
 * 26  | Document status                                                      | VAS_285_DocumentStatus
 * 27  | Delivery status                                                      | VAS_285_DeliveryStatus
 * 28  | Sales order lines                                                    | VAS_285_SalesOrderLines
 * 29  | Line                                                                 | VAS_285_ColLine
 * 30  | Product                                                              | VAS_285_ColProduct
 * 31  | Attribute                                                            | VAS_285_ColAttribute
 * 32  | UoM                                                                  | VAS_285_ColUom
 * 33  | Ordered                                                              | VAS_285_ColOrdered
 * 34  | Delivered                                                            | VAS_285_ColDelivered
 * 35  | Pending                                                              | VAS_285_ColPending
 * 36  | In stock                                                             | VAS_285_ColInStock
 * 37  | Rate                                                                 | VAS_285_ColRate
 * 38  | Amount                                                               | VAS_285_ColAmount
 * 39  | Line status                                                          | VAS_285_ColLineStatus
 * 40  | Delivered                                                            | VAS_285_DeliveryFull
 * 41  | Partially delivered                                                  | VAS_285_DeliveryPartial
 * 42  | Not delivered                                                        | VAS_285_DeliveryNone
 * 43  | Partly delivered                                                     | VAS_285_LinePartial
 * 44  | In process                                                           | VAS_285_LineInProcess
 * 45  | Sales order                                                          | VAS_285_SalesOrderPrefix
 * 46  | lines                                                                | VAS_285_LinesSuffix
 * 47  | qty ordered                                                          | VAS_285_QtyOrderedSuffix
 * 48  | qty short of stock                                                   | VAS_285_QtyShortSuffix
 * 49  | Previous page                                                        | VAS_285_PrevPage
 * 50  | Next page                                                            | VAS_285_NextPage
 * 51  | of                                                                   | VAS_285_Of
 * 52  | Showing                                                              | VAS_285_Showing
 * 53  | Search is unavailable right now. Try again in a moment.              | VAS_285_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var TOP_COUNT = 4;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MIN_YEAR = 2024;
    var YEAR_SPAN = 3;

    // 4-color cycle by rank, per the reference bar palette (blue -> teal -> amber -> lilac).
    var BAR_COLORS = ['#A9D2FF', '#A3E0D4', '#FFDCA1', '#CFC9F5'];

    var now = new Date();
    var CUR_M = now.getMonth();
    var CUR_Y = now.getFullYear();

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the widget clamp resolves against the dashboard's visible
       width, not the viewport. A single document-level ResizeObserver serves every
       widget (matches VAS_269-284). */
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

    VAS.VAS_285_TopProductsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas285-root">');
        var $shell, $list, $monthSel, $yearSel;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_285_TopProductsWidget/';

        var rankState = 'loading'; // 'loading' | 'ready' | 'error'
        var products = [];         // up to 4 rows for the current period

        var prodState = null;      // { productId, productName, sku, summary, docs:{page,size,total,rows} }
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
            return Math.round(Number(value)) + '%';
        }

        function parseIso(iso) {
            if (!iso) { return null; }
            var d = new Date(iso + 'T00:00:00');
            return isNaN(d.getTime()) ? null : d;
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
            return '<span class="vas285-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas285-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
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
            $shell = $('<div class="vas285-shell"></div>');

            var $head = $('<div class="vas285-head"></div>');
            var $htxt = $('<div class="vas285-head-txt"></div>');
            $htxt.append('<p class="vas285-title">' + escapeHtml(label('VAS_285_Title', 'Top Products')) + '</p>');
            $htxt.append('<p class="vas285-sub">' + escapeHtml(label('VAS_285_Subtitle', 'By SO value')) + '</p>');

            var $filter = $('<div class="vas285-mfilter"></div>');
            $monthSel = $('<select class="vas285-msel" aria-label="Month"></select>');
            $yearSel = $('<select class="vas285-msel" aria-label="Year"></select>');
            fillMonthSelect($monthSel);
            fillYearSelect($yearSel);
            $filter.append($monthSel, $yearSel);

            $head.append($htxt, $filter);

            $list = $('<div class="vas285-hlist"></div>');

            $shell.append($head, $list);
            $root.append($shell);

            $monthSel.on('click', function (event) { event.stopPropagation(); });
            $yearSel.on('click', function (event) { event.stopPropagation(); });
            $monthSel.on('change', function () { loadTopProducts(); });
            $yearSel.on('change', function () { loadTopProducts(); });

            $list.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-pid]') : null;
                if (row) { openProductModal(Number(row.getAttribute('data-pid')), row.getAttribute('data-pname'), row.getAttribute('data-psku')); }
            });
        }

        function skeletonHtml() {
            var rows = '';
            for (var i = 0; i < TOP_COUNT; i++) {
                rows += '<div class="vas285-hrow vas285-skel-row">' +
                    '<span class="vas285-line"><span class="vas285-skel-nm"></span></span>' +
                    '<span class="vas285-track"><span class="vas285-fill"></span></span></div>';
            }
            return rows;
        }

        function loadTopProducts() {
            rankState = 'loading';
            renderList();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetTopProducts',
                type: 'GET', dataType: 'json', cache: false,
                data: { month: selectedMonth(), year: selectedYear() },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Products) { rankState = 'error'; products = []; }
                    else { rankState = 'ready'; products = parsed.Products; }
                    renderList();
                },
                error: function () {
                    rankState = 'error'; products = [];
                    renderList();
                }
            });
        }

        function renderList() {
            if (rankState === 'loading') {
                $list.html(skeletonHtml());
                return;
            }
            if (rankState === 'error') {
                $list.html('<div class="vas285-empty">' + escapeHtml(label('VAS_285_ErrorState', 'Ranking unavailable')) + '</div>');
                return;
            }
            if (products.length <= 0) {
                $list.html('<div class="vas285-empty">' + escapeHtml(label('VAS_285_ZeroState', 'No product demand recorded')) + '</div>');
                return;
            }

            // Fewer than TOP_COUNT rows (sparse period data): space-between would
            // stretch 1-3 rows to the two extreme edges of the cell, leaving a large
            // dead gap in the middle (the VAS_278 lesson). Stack from the top instead.
            $list.toggleClass('vas285-hlist-top', products.length < TOP_COUNT);

            var topValue = Number(products[0].OrderValue || 0);

            $list.html(products.map(function (p, i) {
                var pct = topValue > 0 ? Math.max(2, Math.round((Number(p.OrderValue || 0) / topValue) * 100)) : 0;
                var color = BAR_COLORS[i % BAR_COLORS.length];
                return '<button type="button" class="vas285-hrow" data-pid="' + p.ProductId + '" data-pname="' + escapeHtml(p.ProductName) + '" data-psku="' + escapeHtml(p.Sku) + '">' +
                    '<span class="vas285-line"><span class="vas285-nm" title="' + escapeHtml(p.ProductName) + '">' + escapeHtml(p.ProductName) + '</span>' +
                    '<span class="vas285-vl" title="' + escapeHtml(formatINR(p.OrderValue)) + '">' + escapeHtml(formatINR(p.OrderValue)) + '</span></span>' +
                    '<span class="vas285-track"><span class="vas285-fill" style="width:' + pct + '%;background:' + color + '"></span></span></button>';
            }).join(''));
        }

        /* ============================================================
         * Modal shell - lives on <body>, not inside $root, so the widget
         * cell's own overflow cannot clip it. Every screen is a
         * { title, subtitle, size, render } config; showScreen() pushes the
         * outgoing config onto cfgStack and paints the new one; backModal()
         * pops and re-renders the popped config from its own live state
         * (prodState / lineState) - never a frozen HTML snapshot.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas285-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas285-modal"></div>');
            $mHead = $('<div class="vas285-mhead"></div>');
            var $htxt = $('<div class="vas285-htxt"></div>');
            $mBack = $('<button type="button" class="vas285-xbtn" aria-label="' + escapeHtml(label('VAS_285_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas285-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas285-xbtn" aria-label="' + escapeHtml(label('VAS_285_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas285-mbody"></div>');
            $mFoot = $('<div class="vas285-mfoot"></div>');

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
            var ns = '.vas285-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            prodState = null;
            lineState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || '');
            $mSub.text('');
            $mBody.html('<div class="vas285-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas285-mstate">' + escapeHtml(label('VAS_285_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Product drill-down (top-level modal opened from a row click)
         * ============================================================ */
        function openProductModal(productId, productName, sku) {
            showLoading(productName);

            fetchProductDemand(productId, function (summaryOk, summary) {
                if (!summaryOk) { showLoadError(); return; }
                fetchProductOrders(productId, 0, MAX_ROWS_PER_PAGE, function (ordersOk, total, rows) {
                    if (!ordersOk) { showLoadError(); return; }
                    prodState = {
                        productId: productId,
                        productName: productName,
                        sku: sku,
                        summary: summary,
                        docs: { page: 0, size: MAX_ROWS_PER_PAGE, total: total, rows: rows }
                    };
                    showScreen(buildProductCfg(productName, sku), false);
                });
            });
        }

        function buildProductCfg(productName, sku) {
            return {
                title: productName,
                subtitle: label('VAS_285_SubtitlePrefix', 'Sales orders carrying this product') + ' · ' + (sku || ''),
                size: '',
                render: renderProductBody
            };
        }

        function fetchProductDemand(productId, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetProductDemand',
                type: 'GET', dataType: 'json', cache: false,
                data: { productId: productId, month: selectedMonth(), year: selectedYear() },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { cb(false); return; }
                    cb(true, parsed);
                },
                error: function () { cb(false); }
            });
        }

        function fetchProductOrders(productId, page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetProductOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { productId: productId, month: selectedMonth(), year: selectedYear(), page: page, size: size },
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
            if (code === 'FULL') { return 'vas285-chip-ok'; }
            if (code === 'PARTIAL') { return 'vas285-chip-warn'; }
            return 'vas285-chip-neutral';
        }

        function renderProductBody() {
            var summary = prodState.summary;
            var statsHtml = '<div class="vas285-mstats">' +
                statTile(label('VAS_285_StatOrderValue', 'SO value'), formatINR(summary.OrderValue)) +
                statTile(label('VAS_285_StatOpenQty', 'Qty on open SO'), formatNum(summary.OpenQty) + (summary.UomName ? ' ' + summary.UomName : '')) +
                statTile(label('VAS_285_StatFreeStock', 'Free stock'), formatNum(summary.FreeStock) + (summary.UomName ? ' ' + summary.UomName : '')) +
                statTile(label('VAS_285_StatCoverage', 'Coverage'), formatPercent(summary.CoveragePercent)) +
            '</div>';

            var secHtml = '<div class="vas285-msec">' + escapeHtml(label('VAS_285_SectionHeading', 'Sales orders')) + '</div>';

            $mBody.html(statsHtml + secHtml + '<div class="vas285-mtwrap"><div class="vas285-mtbl" id="vas285-docstbl"></div></div>');
            drawDocumentsTable();

            $mFoot.html('<span class="vas285-foot-note"></span><span><button type="button" class="vas285-btn" id="vas285-mclose">' + escapeHtml(label('VAS_285_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas285-mclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_285_ColSoNo', 'SO No'), w: 1.2 },
            { key: 'date', label: label('VAS_285_SoDate', 'SO date'), w: 1 },
            { key: 'customer', label: label('VAS_285_Customer', 'Customer'), w: 1.7 },
            { key: 'wh', label: label('VAS_285_ColWarehouse', 'Warehouse'), w: 1.2 },
            { key: 'rep', label: label('VAS_285_Representative', 'Representative'), w: 1.2 },
            { key: 'value', label: label('VAS_285_ColValue', 'Value'), w: .9, align: 'right' },
            { key: 'delivery', label: label('VAS_285_ColDelivery', 'Delivery'), w: 1.05 },
            { key: 'status', label: label('VAS_285_ColStatus', 'Status'), w: 1.1 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas285-docstbl');
            if (!el || !prodState) { return; }
            var docs = prodState.docs;
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docs.total / docs.size));

            var head = '<div class="vas285-mrow vas285-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas285-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docs.rows.map(function (row) {
                var cells = [
                    '<span class="vas285-cell center"><button type="button" class="vas285-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas285-cell"><button type="button" class="vas285-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas285-c-std'),
                    cellHtml(row.CustomerName, 'vas285-c-prim'),
                    cellHtml(row.WarehouseName || '—', 'vas285-c-std'),
                    cellHtml(row.RepresentativeName || '—', 'vas285-c-std'),
                    cellHtml(formatINR(row.OrderValue), 'vas285-c-emph', 'right'),
                    '<span class="vas285-cell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas285-chip ' + deliveryChipClass(row.DeliveryStatusCode) + '">' + escapeHtml(row.DeliveryStatus) + '</span></span>',
                    '<span class="vas285-cell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas285-chip vas285-chip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas285-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docs.page * docs.size;
            var showingLabel = label('VAS_285_Showing', 'Showing') + ' ' + (docs.total ? (start + 1) : 0) + '–' + (start + docs.rows.length) + ' ' + label('VAS_285_Of', 'of') + ' ' + docs.total + ' · ' + label('VAS_285_ClickRowHint', 'click a row to open the record');

            var foot = '<div class="vas285-mtfoot"><span class="vas285-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas285-pager">' +
                        '<button type="button" class="vas285-pbtn" data-table="docs" data-dir="-1"' + (docs.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_285_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas285-ptxt">' + (docs.page + 1) + ' ' + escapeHtml(label('VAS_285_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas285-pbtn" data-table="docs" data-dir="1"' + (docs.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_285_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas285-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs' && prodState) {
                var docs = prodState.docs;
                var pages = Math.max(1, Math.ceil(docs.total / docs.size));
                var next = Math.min(pages - 1, Math.max(0, docs.page + dir));
                if (next === docs.page) { return; }
                fetchProductOrders(prodState.productId, next, docs.size, function (ok, total, rows) {
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_285_DeliveryFull', 'Delivered'), cls: 'vas285-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_285_LinePartial', 'Partly delivered'), cls: 'vas285-chip-warn' }; }
            return { text: label('VAS_285_LineInProcess', 'In process'), cls: 'vas285-chip-neutral' };
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
            showLoading((prodState ? prodState.productName : label('VAS_285_Title', 'Top Products')) + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildRecordCfg(detail.order, detail.lines), true);
            });
        }

        function buildRecordCfg(order, lines) {
            return {
                title: order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateFull(order.SalesOrderDate), order.DocumentStatus].filter(function (p) { return p; }).join(' · '),
                size: '',
                render: function () { renderRecordBody(order, lines); }
            };
        }

        function soHeaderStats(order) {
            return '<div class="vas285-mstats">' +
                statTile(label('VAS_285_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_285_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_285_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_285_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_285_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_285_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_285_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_285_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas285-msec">' + escapeHtml(label('VAS_285_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas285-mtwrap"><div class="vas285-mtbl" id="vas285-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas285-linetbl-record' };
            drawLineTable('vas285-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas285-mback').on('click', backModal);
            $mFoot.find('#vas285-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_285_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_285_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_285_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas285-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas285-btn" id="vas285-mback">' + escapeHtml(label('VAS_285_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas285-btn" id="vas285-mclose">' + escapeHtml(label('VAS_285_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_285_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_285_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateFull(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas285-polink">' + escapeHtml(label('VAS_285_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas285-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas285-mstats">' +
                statTile(label('VAS_285_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_285_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_285_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_285_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas285-msec">' + escapeHtml(label('VAS_285_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas285-mtwrap"><div class="vas285-mtbl" id="vas285-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas285-linetbl-lines' };
            drawLineTable('vas285-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas285-mback').on('click', backModal);
            $mFoot.find('#vas285-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_285_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_285_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_285_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_285_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_285_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_285_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_285_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_285_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_285_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_285_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_285_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas285-mrow vas285-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas285-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas285-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas285-c-prim'),
                    cellHtml(line.AttributeText, 'vas285-c-std'),
                    cellHtml(line.UomName, 'vas285-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas285-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas285-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas285-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas285-c-short' : 'vas285-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas285-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas285-c-emph', 'right')
                ].join('') + '<span class="vas285-cell"><span class="vas285-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas285-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_285_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_285_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas285-mtfoot"><span class="vas285-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas285-pager">' +
                        '<button type="button" class="vas285-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_285_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas285-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_285_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas285-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_285_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas285-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. */
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas285-mhead-row');
            var foot = el.querySelector('.vas285-mtfoot');
            var row = el.querySelector('.vas285-mbody-rows .vas285-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            if (prodState) {
                fitTable('vas285-docstbl', prodState.docs.size, function (n) {
                    var docs = prodState.docs;
                    var newPages = Math.max(1, Math.ceil(docs.total / n));
                    var page = Math.min(docs.page, newPages - 1);
                    fetchProductOrders(prodState.productId, page, n, function (ok, total, rows) {
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
            loadTopProducts();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas285-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadTopProducts(); };
    };

    VAS.VAS_285_TopProductsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_285_TopProductsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_285_TopProductsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_285_TopProductsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_285_TopProductsWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
