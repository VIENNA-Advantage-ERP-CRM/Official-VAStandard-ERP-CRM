/**
 * VAS_283 Warehouse Wise SO · Document Status Widget (Sales Order dashboard, 4x2 cross-tab table)
 * Purpose - A status matrix: for a selected Month/Year, how many Sales Orders sit in
 *           each stage of the document lifecycle, per ship-from warehouse. The four
 *           columns are a MUTUALLY EXCLUSIVE display classification, never altering
 *           the stored DocStatus: Drafted (DR), In process (IP), Partly delivered
 *           (DocStatus='CO' with 0 &lt; delivered qty &lt; ordered qty - the same
 *           derivation as the pending-delivery KPIs), and Completed (every other
 *           DocStatus='CO' order). CL/VO/RE and everything else are excluded
 *           entirely, so a row's Total always equals the sum of its four visible
 *           buckets. Only warehouses with at least one qualifying order appear - no
 *           synthesized zero rows.
 *
 * Design  - 16-warehouse-wise-so.html / .md: glass 4x2 tile, header with title only
 *           (no subtitle - the title already names both axes) + a Month/Year period
 *           filter (stopPropagation so it never triggers a row's click handler), a
 *           CSS-grid cross-tab table (6 columns, header and body rows sharing one
 *           identical grid-template-columns string) with 7 warehouse rows per page
 *           and a footer pager. The footer helper carries the GRAND total across
 *           every warehouse, never just the visible page. Row click opens that
 *           warehouse's detail: a 6-card stat strip that reconciles exactly with the
 *           row, plus its Sales Orders for the period (standard SO column set),
 *           reusing the same shared record/lines child modals as every other widget
 *           on this dashboard.
 *
 * Backend - VAS_283_WarehouseWiseSODocumentStatusWidget/GetWarehouseStatus     (GET month,year,page,size -> ranked page + grand total)
 *           VAS_283_WarehouseWiseSODocumentStatusWidget/GetWarehouseOrders     (GET warehouseId,month,year,page,size -> stat strip + paginated SOs)
 *           VAS_283_WarehouseWiseSODocumentStatusWidget/GetSalesOrderDetail    (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Warehouse Wise SO · Document Status                                   | VAS_283_Title
 *  2  | No sales orders in                                                    | VAS_283_ZeroStatePrefix
 *  3  | Warehouse summary unavailable                                         | VAS_283_ErrorState
 *  4  | Warehouse                                                             | VAS_283_ColWarehouse
 *  5  | Drafted                                                               | VAS_283_ColDrafted
 *  6  | In process                                                            | VAS_283_ColInProcess
 *  7  | Partly delivered                                                      | VAS_283_ColPartlyDelivered
 *  8  | Completed                                                             | VAS_283_ColCompleted
 *  9  | Total                                                                 | VAS_283_ColTotal
 * 10  | open SOs across all warehouses                                        | VAS_283_OpenSosSuffix
 * 11  | Sales orders shipping from this warehouse                             | VAS_283_SubtitlePrefix
 * 12  | Total SOs                                                             | VAS_283_StatTotalSos
 * 13  | Period                                                                | VAS_283_StatPeriod
 * 14  | Sales orders                                                          | VAS_283_SectionHeading
 * 15  | SO No                                                                 | VAS_283_ColSoNo
 * 16  | SO date                                                               | VAS_283_SoDate
 * 17  | Customer                                                              | VAS_283_Customer
 * 18  | Representative                                                        | VAS_283_Representative
 * 19  | Value                                                                 | VAS_283_ColValue
 * 20  | Delivery                                                              | VAS_283_ColDelivery
 * 21  | Status                                                                | VAS_283_ColStatus
 * 22  | Back                                                                  | VAS_283_Back
 * 23  | Close                                                                 | VAS_283_Close
 * 24  | Date promised                                                         | VAS_283_DatePromised
 * 25  | SO value                                                              | VAS_283_SoValue
 * 26  | Ship from                                                             | VAS_283_ShipFrom
 * 27  | Delivery mode                                                         | VAS_283_DeliveryMode
 * 28  | Document status                                                       | VAS_283_DocumentStatus
 * 29  | Delivery status                                                       | VAS_283_DeliveryStatus
 * 30  | Sales order lines                                                     | VAS_283_SalesOrderLines
 * 31  | Line                                                                  | VAS_283_ColLine
 * 32  | Product                                                               | VAS_283_ColProduct
 * 33  | Attribute                                                             | VAS_283_ColAttribute
 * 34  | UoM                                                                   | VAS_283_ColUom
 * 35  | Ordered                                                               | VAS_283_ColOrdered
 * 36  | Delivered                                                             | VAS_283_ColDelivered
 * 37  | Pending                                                               | VAS_283_ColPending
 * 38  | In stock                                                              | VAS_283_ColInStock
 * 39  | Rate                                                                  | VAS_283_ColRate
 * 40  | Amount                                                                | VAS_283_ColAmount
 * 41  | Line status                                                           | VAS_283_ColLineStatus
 * 42  | Fully delivered                                                       | VAS_283_DeliveryFull
 * 43  | Partial                                                               | VAS_283_DeliveryPartial
 * 44  | Pending                                                               | VAS_283_DeliveryPending
 * 45  | Partly delivered                                                      | VAS_283_LinePartial
 * 46  | In process                                                            | VAS_283_LineInProcess
 * 47  | Sales order                                                           | VAS_283_SalesOrderPrefix
 * 48  | lines                                                                 | VAS_283_LinesSuffix
 * 49  | qty ordered                                                           | VAS_283_QtyOrderedSuffix
 * 50  | qty short of stock                                                    | VAS_283_QtyShortSuffix
 * 51  | Previous page                                                         | VAS_283_PrevPage
 * 52  | Next page                                                             | VAS_283_NextPage
 * 53  | of                                                                    | VAS_283_Of
 * 54  | Showing                                                               | VAS_283_Showing
 * 55  | Search is unavailable right now. Try again in a moment.               | VAS_283_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var WIDGET_PAGE_SIZE = 7;   // warehouse rows per widget page
    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MIN_YEAR = 2024;
    var YEAR_SPAN = 3;

    // Header row + every body row share this exact grid-template-columns string
    // (design.md §10/§11): Warehouse / Drafted / In process / Partly delivered /
    // Completed / Total.
    var TABLE_TEMPLATE = 'minmax(0,1.7fr) minmax(0,.85fr) minmax(0,.9fr) minmax(0,1.15fr) minmax(0,.95fr) minmax(0,.8fr)';

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-282). */
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

    VAS.VAS_283_WarehouseWiseSODocumentStatusWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas283-root">');
        var $shell, $tbody, $helper, $pageTxt, $prevBtn, $nextBtn, $monthSel, $yearSel;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_283_WarehouseWiseSODocumentStatusWidget/';

        var listState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: WIDGET_PAGE_SIZE, total: 0, rows: [], grandTotal: 0 };

        var whState = null;      // { warehouseId, warehouseName, summary, docs:{page,size,total,rows} }
        var lineState = null;    // { order, lines, page, size, tableId }
        var cfgStack = [];
        var currentCfg = null;

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

        function selectedMonth() { return $monthSel && $monthSel.length ? Number($monthSel.val()) : new Date().getMonth(); }
        function selectedYear() { return $yearSel && $yearSel.length ? Number($yearSel.val()) : new Date().getFullYear(); }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function mcellHtml(value, cls, align) {
            return '<span class="vas283-mcell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas283-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (title + Month/Year filter) + cross-tab table + pager
         * ============================================================ */
        function fillMonthSelect($sel) {
            var curM = new Date().getMonth();
            $sel.html(MONTHS.map(function (m, i) {
                return '<option value="' + i + '"' + (i === curM ? ' selected' : '') + '>' + m + '</option>';
            }).join(''));
        }

        function fillYearSelect($sel) {
            var curY = new Date().getFullYear();
            var html = '';
            for (var y = MIN_YEAR; y < MIN_YEAR + YEAR_SPAN; y++) {
                html += '<option value="' + y + '"' + (y === curY ? ' selected' : '') + '>' + y + '</option>';
            }
            $sel.html(html);
        }

        function createWidget() {
            $shell = $('<div class="vas283-shell"></div>');

            var $head = $('<div class="vas283-head"></div>');
            var $htxt = $('<div class="vas283-head-txt"></div>');
            $htxt.append('<p class="vas283-title">' + escapeHtml(label('VAS_283_Title', 'Warehouse Wise SO · Document Status')) + '</p>');

            var $filter = $('<div class="vas283-mfilter"></div>');
            $monthSel = $('<select class="vas283-msel" aria-label="Month"></select>');
            $yearSel = $('<select class="vas283-msel" aria-label="Year"></select>');
            fillMonthSelect($monthSel);
            fillYearSelect($yearSel);
            $filter.append($monthSel, $yearSel);

            $head.append($htxt, $filter);

            var $tbl = $('<div class="vas283-tbl"></div>');
            var $thead = $('<div class="vas283-trow vas283-thead"></div>').css('grid-template-columns', TABLE_TEMPLATE);
            $thead.html([
                { l: label('VAS_283_ColWarehouse', 'Warehouse'), right: false },
                { l: label('VAS_283_ColDrafted', 'Drafted'), right: true },
                { l: label('VAS_283_ColInProcess', 'In process'), right: true },
                { l: label('VAS_283_ColPartlyDelivered', 'Partly delivered'), right: true },
                { l: label('VAS_283_ColCompleted', 'Completed'), right: true },
                { l: label('VAS_283_ColTotal', 'Total'), right: true }
            ].map(function (c) {
                return '<span class="vas283-cell' + (c.right ? ' right' : '') + '" title="' + escapeHtml(c.l) + '">' + escapeHtml(c.l) + '</span>';
            }).join(''));

            $tbody = $('<div class="vas283-tbody"></div>');
            $tbl.append($thead, $tbody);

            var $foot = $('<div class="vas283-wfoot"></div>');
            $helper = $('<span class="vas283-helper"></span>');
            var $pager = $('<div class="vas283-pager"></div>');
            $prevBtn = $('<button type="button" class="vas283-pbtn" aria-label="' + escapeHtml(label('VAS_283_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>');
            $pageTxt = $('<span class="vas283-ptxt"></span>');
            $nextBtn = $('<button type="button" class="vas283-pbtn" aria-label="' + escapeHtml(label('VAS_283_NextPage', 'Next page')) + '">' + icon('next') + '</button>');
            $pager.append($prevBtn, $pageTxt, $nextBtn);
            $foot.append($helper, $pager);

            $shell.append($head, $tbl, $foot);
            $root.append($shell);

            $monthSel.on('click', function (event) { event.stopPropagation(); });
            $yearSel.on('click', function (event) { event.stopPropagation(); });
            $monthSel.on('change', function () { loadWarehouseStatus(0); });
            $yearSel.on('change', function () { loadWarehouseStatus(0); });

            $prevBtn.on('click', function () { turnWidgetPage(-1); });
            $nextBtn.on('click', function () { turnWidgetPage(1); });

            $tbody.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-whid]') : null;
                if (row) { openWarehouseModal(Number(row.getAttribute('data-whid')), row.getAttribute('data-whname')); }
            });
        }

        function skeletonRows() {
            var tpl = TABLE_TEMPLATE;
            var rows = '';
            for (var i = 0; i < WIDGET_PAGE_SIZE; i++) {
                rows += '<div class="vas283-trow" style="grid-template-columns:' + tpl + ';cursor:default">' +
                    '<span class="vas283-cell"><span class="vas283-skel-cell" style="width:70%"></span></span>' +
                    '<span class="vas283-cell right"><span class="vas283-skel-cell" style="width:40%;margin-left:auto"></span></span>' +
                    '<span class="vas283-cell right"><span class="vas283-skel-cell" style="width:40%;margin-left:auto"></span></span>' +
                    '<span class="vas283-cell right"><span class="vas283-skel-cell" style="width:50%;margin-left:auto"></span></span>' +
                    '<span class="vas283-cell right"><span class="vas283-skel-cell" style="width:40%;margin-left:auto"></span></span>' +
                    '<span class="vas283-cell right"><span class="vas283-skel-cell" style="width:40%;margin-left:auto"></span></span>' +
                '</div>';
            }
            return rows;
        }

        function loadWarehouseStatus(page) {
            listState = 'loading';
            renderWidget();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetWarehouseStatus',
                type: 'GET', dataType: 'json', cache: false,
                data: { month: selectedMonth(), year: selectedYear(), page: page, size: WIDGET_PAGE_SIZE },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { listState = 'error'; }
                    else {
                        listState = 'ready';
                        docsState.page = page;
                        docsState.total = Number(parsed.Total || 0);
                        docsState.grandTotal = Number(parsed.GrandTotal || 0);
                        docsState.rows = parsed.Rows || [];
                    }
                    renderWidget();
                },
                error: function () {
                    listState = 'error';
                    renderWidget();
                }
            });
        }

        function renderWidget() {
            if (listState === 'loading') {
                $tbody.html(skeletonRows());
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }
            if (listState === 'error') {
                $tbody.html('<div class="vas283-empty">' + escapeHtml(label('VAS_283_ErrorState', 'Warehouse summary unavailable')) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }
            if (docsState.total <= 0) {
                $tbody.html('<div class="vas283-empty">' + escapeHtml(label('VAS_283_ZeroStatePrefix', 'No sales orders in') + ' ' + periodLabel()) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            var tpl = TABLE_TEMPLATE;
            $tbody.html(docsState.rows.map(function (row) {
                return '<button type="button" class="vas283-trow" style="grid-template-columns:' + tpl + '" data-whid="' + row.WarehouseId + '" data-whname="' + escapeHtml(row.WarehouseName) + '">' +
                    '<span class="vas283-cell vas283-c-prim" title="' + escapeHtml(row.WarehouseName) + '">' + escapeHtml(row.WarehouseName) + '</span>' +
                    '<span class="vas283-cell right vas283-c-std" title="' + row.DraftedCount + '">' + formatNum(row.DraftedCount) + '</span>' +
                    '<span class="vas283-cell right vas283-c-dark" title="' + row.InProcessCount + '">' + formatNum(row.InProcessCount) + '</span>' +
                    '<span class="vas283-cell right vas283-c-dark" title="' + row.PartlyDeliveredCount + '">' + formatNum(row.PartlyDeliveredCount) + '</span>' +
                    '<span class="vas283-cell right vas283-c-std" title="' + row.CompletedCount + '">' + formatNum(row.CompletedCount) + '</span>' +
                    '<span class="vas283-cell right vas283-c-emph" title="' + row.TotalCount + '">' + formatNum(row.TotalCount) + '</span>' +
                '</button>';
            }).join(''));

            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var start = docsState.page * docsState.size;
            var helperText = label('VAS_283_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_283_Of', 'of') + ' ' + docsState.total + ' · ' + formatNum(docsState.grandTotal) + ' ' + label('VAS_283_OpenSosSuffix', 'open SOs across all warehouses');
            $helper.attr('title', helperText).text(helperText);
            $pageTxt.text((docsState.page + 1) + ' ' + label('VAS_283_Of', 'of') + ' ' + pages);
            $prevBtn.prop('disabled', docsState.page === 0);
            $nextBtn.prop('disabled', docsState.page >= pages - 1);
        }

        function turnWidgetPage(dir) {
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var next = Math.min(pages - 1, Math.max(0, docsState.page + dir));
            if (next === docsState.page) { return; }
            loadWarehouseStatus(next);
        }

        /* ============================================================
         * Modal shell - lives on <body>, not inside $root, so the widget
         * cell's own overflow cannot clip it. Every screen is a
         * { title, subtitle, size, render } config; showScreen() pushes the
         * outgoing config onto cfgStack and paints the new one; backModal()
         * pops and re-renders the popped config from its own live state
         * (whState / lineState) - never a frozen HTML snapshot.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas283-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas283-modal"></div>');
            $mHead = $('<div class="vas283-mhead"></div>');
            var $htxt = $('<div class="vas283-htxt"></div>');
            $mBack = $('<button type="button" class="vas283-xbtn" aria-label="' + escapeHtml(label('VAS_283_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas283-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas283-xbtn" aria-label="' + escapeHtml(label('VAS_283_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas283-mbody"></div>');
            $mFoot = $('<div class="vas283-mfoot"></div>');

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
            var ns = '.vas283-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            whState = null;
            lineState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || '');
            $mSub.text('');
            $mBody.html('<div class="vas283-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas283-mstate">' + escapeHtml(label('VAS_283_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Warehouse drill-down (top-level modal opened from a row click)
         * ============================================================ */
        function openWarehouseModal(warehouseId, warehouseName) {
            showLoading(warehouseName);

            fetchWarehouseOrders(warehouseId, 0, MAX_ROWS_PER_PAGE, function (ok, summary, total, rows) {
                if (!ok) { showLoadError(); return; }
                whState = { warehouseId: warehouseId, warehouseName: warehouseName, summary: summary, docs: { page: 0, size: MAX_ROWS_PER_PAGE, total: total, rows: rows } };
                showScreen(buildWarehouseCfg(warehouseName), false);
            });
        }

        function buildWarehouseCfg(warehouseName) {
            return {
                title: warehouseName,
                subtitle: label('VAS_283_SubtitlePrefix', 'Sales orders shipping from this warehouse') + ' · ' + periodLabel(),
                size: '',
                render: renderWarehouseBody
            };
        }

        function fetchWarehouseOrders(warehouseId, page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetWarehouseOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { warehouseId: warehouseId, month: selectedMonth(), year: selectedYear(), page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Summary) { cb(false); return; }
                    cb(true, parsed.Summary, Number(parsed.Total || 0), (parsed.Rows || []).map(normalizeOrderRow));
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
            if (code === 'FULL') { return 'vas283-mchip-info'; }
            if (code === 'PARTIAL') { return 'vas283-mchip-warn'; }
            return 'vas283-mchip-neutral';
        }

        function renderWarehouseBody() {
            var summary = whState.summary;
            var statsHtml = '<div class="vas283-mstats">' +
                statTile(label('VAS_283_StatTotalSos', 'Total SOs'), formatNum(summary.TotalCount)) +
                statTile(label('VAS_283_ColDrafted', 'Drafted'), formatNum(summary.DraftedCount)) +
                statTile(label('VAS_283_ColInProcess', 'In process'), formatNum(summary.InProcessCount)) +
                statTile(label('VAS_283_ColPartlyDelivered', 'Partly delivered'), formatNum(summary.PartlyDeliveredCount)) +
                statTile(label('VAS_283_ColCompleted', 'Completed'), formatNum(summary.CompletedCount)) +
                statTile(label('VAS_283_StatPeriod', 'Period'), periodLabel()) +
            '</div>';

            var secHtml = '<div class="vas283-msec">' + escapeHtml(label('VAS_283_SectionHeading', 'Sales orders')) + '</div>';

            if (whState.docs.total <= 0) {
                $mBody.html(statsHtml + secHtml + '<div class="vas283-mstate">' + escapeHtml(label('VAS_283_ZeroStatePrefix', 'No sales orders in') + ' ' + periodLabel()) + '</div>');
            } else {
                $mBody.html(statsHtml + secHtml + '<div class="vas283-mtwrap"><div class="vas283-mtbl" id="vas283-docstbl"></div></div>');
                drawDocumentsTable();
            }

            $mFoot.html('<span class="vas283-foot-note"></span><span><button type="button" class="vas283-btn" id="vas283-mclose">' + escapeHtml(label('VAS_283_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas283-mclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_283_ColSoNo', 'SO No'), w: 1.2 },
            { key: 'date', label: label('VAS_283_SoDate', 'SO date'), w: 1 },
            { key: 'customer', label: label('VAS_283_Customer', 'Customer'), w: 1.7 },
            { key: 'wh', label: label('VAS_283_ColWarehouse', 'Warehouse'), w: 1.2 },
            { key: 'rep', label: label('VAS_283_Representative', 'Representative'), w: 1.2 },
            { key: 'value', label: label('VAS_283_ColValue', 'Value'), w: .9, align: 'right' },
            { key: 'delivery', label: label('VAS_283_ColDelivery', 'Delivery'), w: 1.05 },
            { key: 'status', label: label('VAS_283_ColStatus', 'Status'), w: 1.1 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas283-docstbl');
            if (!el || !whState) { return; }
            var docs = whState.docs;
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docs.total / docs.size));

            var head = '<div class="vas283-mrow vas283-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas283-mcell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docs.rows.map(function (row) {
                var cells = [
                    '<span class="vas283-mcell center"><button type="button" class="vas283-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas283-mcell"><button type="button" class="vas283-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    mcellHtml(formatDateFull(row.SalesOrderDate), 'vas283-c-std'),
                    mcellHtml(row.CustomerName, 'vas283-c-prim'),
                    mcellHtml(row.WarehouseName || '—', 'vas283-c-std'),
                    mcellHtml(row.RepresentativeName || '—', 'vas283-c-std'),
                    mcellHtml(formatINR(row.OrderValue), 'vas283-c-emph', 'right'),
                    '<span class="vas283-mcell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas283-mchip ' + deliveryChipClass(row.DeliveryStatusCode) + '">' + escapeHtml(row.DeliveryStatus) + '</span></span>',
                    '<span class="vas283-mcell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas283-mchip vas283-mchip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas283-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docs.page * docs.size;
            var showingLabel = label('VAS_283_Showing', 'Showing') + ' ' + (docs.total ? (start + 1) : 0) + '–' + (start + docs.rows.length) + ' ' + label('VAS_283_Of', 'of') + ' ' + docs.total;

            var foot = '<div class="vas283-mtfoot"><span class="vas283-mhelper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas283-mpager">' +
                        '<button type="button" class="vas283-mpbtn" data-table="docs" data-dir="-1"' + (docs.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_283_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas283-mptxt">' + (docs.page + 1) + ' ' + escapeHtml(label('VAS_283_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas283-mpbtn" data-table="docs" data-dir="1"' + (docs.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_283_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas283-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs' && whState) {
                var docs = whState.docs;
                var pages = Math.max(1, Math.ceil(docs.total / docs.size));
                var next = Math.min(pages - 1, Math.max(0, docs.page + dir));
                if (next === docs.page) { return; }
                fetchWarehouseOrders(whState.warehouseId, next, docs.size, function (ok, summary, total, rows) {
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_283_DeliveryFull', 'Fully delivered'), cls: 'vas283-mchip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_283_LinePartial', 'Partly delivered'), cls: 'vas283-mchip-warn' }; }
            return { text: label('VAS_283_LineInProcess', 'In process'), cls: 'vas283-mchip-neutral' };
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
            showLoading((whState ? whState.warehouseName : label('VAS_283_Title', 'Warehouse Wise SO · Document Status')) + '…');
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
            return '<div class="vas283-mstats">' +
                statTile(label('VAS_283_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_283_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_283_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_283_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_283_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_283_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_283_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_283_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas283-msec">' + escapeHtml(label('VAS_283_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas283-mtwrap"><div class="vas283-mtbl" id="vas283-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas283-linetbl-record' };
            drawLineTable('vas283-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas283-mback').on('click', backModal);
            $mFoot.find('#vas283-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_283_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_283_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_283_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas283-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas283-btn" id="vas283-mback">' + escapeHtml(label('VAS_283_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas283-btn" id="vas283-mclose">' + escapeHtml(label('VAS_283_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_283_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_283_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateFull(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas283-polink">' + escapeHtml(label('VAS_283_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas283-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas283-mstats">' +
                statTile(label('VAS_283_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_283_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_283_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_283_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas283-msec">' + escapeHtml(label('VAS_283_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas283-mtwrap"><div class="vas283-mtbl" id="vas283-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas283-linetbl-lines' };
            drawLineTable('vas283-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas283-mback').on('click', backModal);
            $mFoot.find('#vas283-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_283_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_283_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_283_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_283_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_283_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_283_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_283_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_283_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_283_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_283_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_283_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas283-mrow vas283-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas283-mcell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    mcellHtml(String(line.LineNo), 'vas283-c-std', 'right'),
                    mcellHtml(line.ProductName, 'vas283-c-prim'),
                    mcellHtml(line.AttributeText, 'vas283-c-std'),
                    mcellHtml(line.UomName, 'vas283-c-std'),
                    mcellHtml(formatNum(line.QtyOrdered), 'vas283-c-std', 'right'),
                    mcellHtml(formatNum(line.QtyDelivered), 'vas283-c-std', 'right'),
                    mcellHtml(formatNum(line.QtyPending), 'vas283-c-prim', 'right'),
                    mcellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas283-c-short' : 'vas283-c-ok'), 'right'),
                    mcellHtml('₹ ' + formatNum(line.Rate), 'vas283-c-std', 'right'),
                    mcellHtml(formatINR(line.Amount), 'vas283-c-emph', 'right')
                ].join('') + '<span class="vas283-mcell"><span class="vas283-mchip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas283-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_283_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_283_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas283-mtfoot"><span class="vas283-mhelper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas283-mpager">' +
                        '<button type="button" class="vas283-mpbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_283_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas283-mptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_283_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas283-mpbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_283_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas283-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. */
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas283-mhead-row');
            var foot = el.querySelector('.vas283-mtfoot');
            var row = el.querySelector('.vas283-mbody-rows .vas283-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            if (whState) {
                fitTable('vas283-docstbl', whState.docs.size, function (n) {
                    var docs = whState.docs;
                    var newPages = Math.max(1, Math.ceil(docs.total / n));
                    var page = Math.min(docs.page, newPages - 1);
                    fetchWarehouseOrders(whState.warehouseId, page, n, function (ok, summary, total, rows) {
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
            loadWarehouseStatus(0);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas283-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadWarehouseStatus(0); };
    };

    VAS.VAS_283_WarehouseWiseSODocumentStatusWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_283_WarehouseWiseSODocumentStatusWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_283_WarehouseWiseSODocumentStatusWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_283_WarehouseWiseSODocumentStatusWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_283_WarehouseWiseSODocumentStatusWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
