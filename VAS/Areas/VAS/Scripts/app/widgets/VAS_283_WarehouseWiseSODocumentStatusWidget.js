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

    /* Sales Order dashboard request queue (shared by VAS_270-289).
       The dashboard starts ~20 widget calls at once on one login session and the server
       answers the overflow with HTTP 500 ("request queue limit of the session exceeded").
       GET calls are therefore run at most 3 at a time and retried (up to 2 times, with a
       short back-off) when the server answers 500/503/0. The first widget file loaded
       creates the singleton; every other widget reuses it. A failed card also gets a
       Retry button so the user never has to close and re-open the screen. */
    var VASSONet = window.__vasSONet = window.__vasSONet || (function () {
        var MAX_PARALLEL = 3, MAX_RETRIES = 2, running = 0, waiting = [];

        function pump() {
            while (running < MAX_PARALLEL && waiting.length) { start(waiting.shift()); }
        }

        function start(job) {
            running++;
            var o = job.opts;
            $.ajax($.extend({}, o, {
                success: function () {
                    running--;
                    if (o.success) { o.success.apply(this, arguments); }
                    pump();
                },
                error: function (xhr) {
                    var self = this, args = arguments;
                    running--;
                    var st = xhr ? xhr.status : 0;
                    if (job.tries < MAX_RETRIES && (st === 500 || st === 503 || st === 0) && xhr.statusText !== 'abort') {
                        job.tries++;
                        setTimeout(function () { waiting.push(job); pump(); }, 400 * job.tries + Math.floor(Math.random() * 300));
                    } else if (o.error) {
                        o.error.apply(self, args);
                    }
                    pump();
                }
            }));
        }

        function injectStyle() {
            if (document.getElementById('vasso-retry-style')) { return; }
            var st = document.createElement('style');
            st.id = 'vasso-retry-style';
            st.textContent = '.vasso-retry{position:absolute;right:10px;bottom:8px;z-index:3;border:1px solid currentColor;' +
                'background:rgba(255,255,255,.85);color:#1a5fb4;border-radius:12px;padding:2px 12px;font-size:12px;' +
                'line-height:18px;cursor:pointer}.vasso-retry:hover,.vasso-retry:focus{background:#fff;outline:2px solid #1a5fb4}';
            document.head.appendChild(st);
        }

        return {
            // GET calls are queued/retried; anything else (e.g. a POST that creates a record) runs untouched.
            ajax: function (o) {
                if (o && o.type && String(o.type).toUpperCase() !== 'GET') { return $.ajax(o); }
                waiting.push({ opts: o, tries: 0 });
                pump();
            },
            // Adds a Retry button to a widget root that is showing its error state.
            retry: function ($root, widget) {
                injectStyle();
                $root.find('.vasso-retry').remove();
                if ($root.css('position') === 'static') { $root.css('position', 'relative'); }
                var text = '';
                try { text = VIS.Msg.getMsg('VAS_SO_Retry'); } catch (e) { text = ''; }
                if (!text || text === 'VAS_SO_Retry') { text = 'Retry'; }
                $('<button type="button" class="vasso-retry"></button>').text(text)
                    .attr('title', text)
                    .on('click', function (e) {
                        e.stopPropagation();
                        $(this).remove();
                        widget.refreshWidget();
                    })
                    .appendTo($root);
            }
        };
    })();


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
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot, $mClose;

        var SELF_ENDPOINT = 'VAS_283_WarehouseWiseSODocumentStatusWidget/';
        // VAS163 2026-11-11: Row 115 - per-instance event-namespace suffix (as VAS_279 Sr 8.28):
        // disposing an old instance must not unbind the handlers of a re-created one.
        var instanceTag = 'i' + Math.random().toString(36).slice(2, 9);
        var ZOOM_WINDOW_NAME = 'VAS_SalesOrder';   // same Sales Order window VAS_268 / VAS_286 open
        var zoomWindowId = 0;

        var listState = 'loading'; // 'loading' | 'ready' | 'error'
        var currency = { iso: '', symbol: '', precision: null }; // VAS163: base currency from GetWarehouseStatus
        var docsState = { page: 0, size: WIDGET_PAGE_SIZE, total: 0, rows: [], grandTotal: 0 };

        var whState = null;      // { warehouseId, warehouseName, summary, docs:{page,size,total,rows} }
        var lineState = null;    // { order, lines, page, size, tableId }
        var cfgStack = [];
        var currentCfg = null;

        // Never render a raw "[KEY]": fall back to the English text when the key is missing.
        function label(key, fallback) {
            var text = '';
            try { text = VIS.Msg.getMsg(key); } catch (e) { text = ''; }
            if (!text || text === key || text === '[' + key + ']') { return fallback || key; }
            return text;
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

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function applyCurrency(data) {
            if (!data) { return; }
            currency.iso = data.CurrencyIso || '';
            currency.symbol = data.CurrencySymbol || '';
            currency.precision = (typeof data.CurrencyPrecision === 'number') ? data.CurrencyPrecision : null;
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatINR(value) {
            var num = Number(value || 0);
            var sym = currency.symbol || currency.iso || '';
            var text = (num < 0 ? '-' : '') + VIS.Util.formatCompactAmount(Math.abs(num), currency.iso, currency.precision);
            return sym ? sym + ' ' + text : text;
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatNum(value) {
            return Math.round(Number(value || 0)).toLocaleString(window.navigator.language);
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatMoney(value, symbol, precision) {
            var prec = (typeof precision === 'number' && precision >= 0) ? precision : 2;
            var text = Number(value || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return symbol ? symbol + ' ' + text : text;
        }

        // VAS163 2026-11-11: Row 117 - quantities in the line's UOM can be fractional (e.g. ml).
        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        // VAS163 2026-11-11: Row 121 - a description made only of blanks / separators is not
        // an attribute value (same rule as VAS_270 / VAS_274).
        function hasAttributeValue(text) {
            return !!text && /[^\s\-‐-―_.,:;()\[\]\/\\|]/.test(String(text));
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function parseIso(iso) {
            var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso || ''));
            if (!m) { return null; }
            var d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
            return isNaN(d.getTime()) ? null : d;
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatDateFull(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return d.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function periodLabel() {
            return new Date(selectedYear(), selectedMonth(), 1).toLocaleDateString(window.navigator.language, { month: 'short', year: 'numeric' });
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
        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function fillMonthSelect($sel) {
            var curM = new Date().getMonth();
            $sel.html(MONTHS.map(function (m, i) {
                m = new Date(2000, i, 1).toLocaleDateString(window.navigator.language, { month: 'short' });
                return '<option value="' + i + '"' + (i === curM ? ' selected' : '') + '>' + escapeHtml(m) + '</option>';
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
            bindWidgetEvents();
        }

        /* VAS163 2026-11-11: Row 115 - (re)binds the widget's own handlers. Run on init AND
           on every refresh (same fix as VAS_270 / VAS_279 / VAS_285 "refresh all"): the
           mosaic refresh can detach / strip jQuery handlers from the widget's DOM, which
           left a widget that showed nothing until the page was reloaded. off() first keeps
           it to one handler each. */
        function bindWidgetEvents() {
            if (!$shell) { return; }
            $monthSel.off('.vas283').on('click.vas283', function (event) { event.stopPropagation(); })
                .on('change.vas283', function () { loadWarehouseStatus(0); });
            $yearSel.off('.vas283').on('click.vas283', function (event) { event.stopPropagation(); })
                .on('change.vas283', function () { loadWarehouseStatus(0); });

            $prevBtn.off('click.vas283').on('click.vas283', function () { turnWidgetPage(-1); });
            $nextBtn.off('click.vas283').on('click.vas283', function () { turnWidgetPage(1); });

            $tbody.off('click.vas283').on('click.vas283', function (event) {
                var row = event.target.closest ? event.target.closest('[data-whid]') : null;
                if (row) { openWarehouseModal(Number(row.getAttribute('data-whid')), row.getAttribute('data-whname')); }
            });
        }

        // VAS163 2026-11-11: Row 115 - a table of THIS instance's modal, looked up inside its
        // own body (as VAS_270 tableEl) - never document.getElementById: a modal left on <body>
        // by an earlier instance (re-created by a refresh without disposing) carries the same
        // ids and would be found first, so the new pop-up stayed empty.
        function tableEl(id) {
            return $mBody ? $mBody.find('#' + id)[0] : null;
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

            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetWarehouseStatus',
                type: 'GET', dataType: 'json', cache: false,
                data: { month: selectedMonth(), year: selectedYear(), page: page, size: WIDGET_PAGE_SIZE },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { listState = 'error'; }
                    else {
                        listState = 'ready';
                        applyCurrency(parsed);
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
                VASSONet.retry($root, $self);
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
            $mClose = $closeBtn;
            bindModal();
        }

        /* VAS163 2026-11-11: Row 115 - the modal's own handlers are (re)bound here under one
           namespace, off() first, on init AND on every refresh (VAS_270 bindModal pattern). */
        function bindModal() {
            if (!$mBody) { return; }
            var ns = '.vas283m-' + instanceTag;
            $mClose.off(ns).on('click' + ns, closeModal);
            $mBack.off(ns).on('click' + ns, backModal);
            $mBody.off(ns).on('click' + ns, function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnPage(pageBtn.getAttribute('data-table'), Number(pageBtn.getAttribute('data-dir'))); return; }
                // 5-Nov row 29: a document number opens that SO on the Sales Order screen.
                var soBtn = event.target.closest ? event.target.closest('[data-so]') : null;
                if (soBtn) { openRecordInWindow(Number(soBtn.getAttribute('data-so'))); return; }
                var linesBtn = event.target.closest ? event.target.closest('[data-lines]') : null;
                if (linesBtn) { openLinesModal(Number(linesBtn.getAttribute('data-lines'))); return; }
            });
        }

        function bindDocumentLevelEvents() {
            // VAS163 2026-11-11: Row 115 - instance-tagged namespace.
            var ns = '.vas283-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;

            $(window).off('resize' + ns).on('resize' + ns, function () {
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
            VASSONet.ajax({
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
                // VAS163 2026-11-11: Row 93 - document (price list) currency of OrderValue.
                CurrencySymbol: row.CurrencySymbol || '',
                CurrencyPrecision: (typeof row.CurrencyPrecision === 'number') ? row.CurrencyPrecision : 2,
                DocumentStatus: row.DocumentStatus || '',
                // VAS163 2026-11-11: Row 116 - raw DocStatus for the badge colour.
                DocumentStatusCode: row.DocumentStatusCode || '',
                DeliveryStatus: row.DeliveryStatus || '',
                DeliveryStatusCode: row.DeliveryStatusCode || 'PENDING'
            };
        }

        function deliveryChipClass(code) {
            if (code === 'FULL') { return 'vas283-mchip-info'; }
            if (code === 'PARTIAL') { return 'vas283-mchip-warn'; }
            return 'vas283-mchip-neutral';
        }

        // VAS163 2026-11-11: Row 116 - Drafted / In Progress status badges in orange (as VAS_272).
        function docStatusChipClass(code) {
            return (code === 'DR' || code === 'IP') ? 'vas283-mchip-orange' : 'vas283-mchip-ok';
        }

        function renderWarehouseBody() {
            var summary = whState.summary;
            // 5-Nov rows 27/28: the "Period" tile is gone (the period is already in the
            // subtitle) and the remaining 5 cards share one line (.vas283-mstats-5).
            var statsHtml = '<div class="vas283-mstats vas283-mstats-5">' +
                statTile(label('VAS_283_StatTotalSos', 'Total SOs'), formatNum(summary.TotalCount)) +
                statTile(label('VAS_283_ColDrafted', 'Drafted'), formatNum(summary.DraftedCount)) +
                statTile(label('VAS_283_ColInProcess', 'In process'), formatNum(summary.InProcessCount)) +
                statTile(label('VAS_283_ColPartlyDelivered', 'Partly delivered'), formatNum(summary.PartlyDeliveredCount)) +
                statTile(label('VAS_283_ColCompleted', 'Completed'), formatNum(summary.CompletedCount)) +
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
            var el = tableEl('vas283-docstbl'); // VAS163 2026-11-11: Row 115
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
                    // VAS163 2026-11-11: Rows 93/124 - ex-tax order value with the price-list currency symbol.
                    mcellHtml(formatMoney(row.OrderValue, row.CurrencySymbol, row.CurrencyPrecision), 'vas283-c-emph', 'right'),
                    '<span class="vas283-mcell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas283-mchip ' + deliveryChipClass(row.DeliveryStatusCode) + '">' + escapeHtml(row.DeliveryStatus) + '</span></span>',
                    // VAS163 2026-11-11: Row 116 - Drafted / In Progress badges orange.
                    '<span class="vas283-mcell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas283-mchip ' + docStatusChipClass(row.DocumentStatusCode) + '">' + escapeHtml(row.DocumentStatus) + '</span></span>'
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
            VASSONet.ajax({
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

        /* 5-Nov row 29: navigates to the Sales Order record in the SAME tab. Hosted inside
           the Sales Order window (windowNo >= 0) the host's value-changed channel re-queries
           the window it is already in (TabWhereClause) - same pattern as VAS_268 / VAS_286.
           On the Home page (no host window) the shared cross-window zoom helper opens the
           Sales Order window on that record. */
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
                // VAS163 2026-11-11: Rows 122/123/124 - SO value in full (not K/L/Cr), ex-tax,
                // in the order's price-list currency.
                statTile(label('VAS_283_SoValue', 'SO value'), formatMoney(order.OrderValue, order.CurrencySymbol, order.CurrencyPrecision)) +
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
            // VAS163 2026-11-11: Rows 117/120 - Item lines only, quantities in the line's UOM;
            // shortage stated by the server in the line's UOM (stock never negative).
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                if (!line.IsItem) { return; }
                qtyOrdered += Number(line.QtyOrdered) || 0;
                qtyShort += Math.max(0, Number(line.QtyShort) || 0);
            });
            var footNote = lines.length + ' ' + label('VAS_283_LinesSuffix', 'lines') +
                ' · ' + formatQty(qtyOrdered) + ' ' + label('VAS_283_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatQty(qtyShort) + ' ' + label('VAS_283_QtyShortSuffix', 'qty short of stock') : '');

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
            // VAS163 2026-11-11: Rows 117/120 - the quantity cards count Item-type product lines
            // only (other product types and charges excluded), in the lines' selected UOM.
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) {
                if (!l.IsItem) { return; }
                qtyOrdered += Number(l.QtyOrdered) || 0;
                qtyPending += Number(l.QtyPending) || 0;
            });

            var breadcrumb = '<div class="vas283-polink">' + escapeHtml(label('VAS_283_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas283-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas283-mstats">' +
                statTile(label('VAS_283_ColLine', 'Line') + 's', String(lines.length)) +
                // VAS163 2026-11-11: Rows 122/123/124 - SO value in full, ex-tax, order's currency.
                statTile(label('VAS_283_SoValue', 'SO value'), formatMoney(order.OrderValue, order.CurrencySymbol, order.CurrencyPrecision)) +
                statTile(label('VAS_283_ColOrdered', 'Ordered'), formatQty(qtyOrdered)) +
                statTile(label('VAS_283_ColPending', 'Pending'), formatQty(qtyPending)) +
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

        // VAS163 2026-11-11: In stock is in the product's base UOM; its name is shown when
        // the line was entered in another UOM (as VAS_270 / VAS_274). Never negative.
        function inStockText(line) {
            var text = formatQty(Math.max(0, Number(line.FreeStock) || 0));
            if (line.BaseUomName && line.BaseUomName !== line.UomName) { text += ' ' + line.BaseUomName; }
            return text;
        }

        function drawLineTable(tableId) {
            var el = tableEl(tableId); // VAS163 2026-11-11: Row 115
            if (!el || !lineState) { return; }

            // VAS163 2026-11-11: Row 121 - the Attribute column only when at least one line of
            // the order has an attribute value (decided over all lines, not per page).
            var showAttribute = lineState.lines.some(function (l) { return hasAttributeValue(l.AttributeText); });
            var cols = LINE_COLS.filter(function (c) { return showAttribute || c.key !== 'attr'; });
            // VAS163 2026-11-11: Row 123 - document (price list) currency in front of Rate / Amount.
            var cur = lineState.order.CurrencySymbol;
            var prec = lineState.order.CurrencyPrecision;

            var tpl = cols.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas283-mrow vas283-mhead-row" style="grid-template-columns:' + tpl + '">' +
                cols.map(function (c) { return '<span class="vas283-mcell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                // VAS163 2026-11-11: Row 120 - Ordered / Delivered / Pending / In stock / Line
                // status only for Item-type products; other product types and charges show "-".
                var isItem = !!line.IsItem;
                var st = isItem ? lineStatus(line) : null;
                var cells = [
                    mcellHtml(String(line.LineNo), 'vas283-c-std', 'right'),
                    // VAS163 2026-11-11: Row 118 - charge lines carry the charge name (server).
                    mcellHtml(line.ProductName, 'vas283-c-prim'),
                    showAttribute ? mcellHtml(hasAttributeValue(line.AttributeText) ? line.AttributeText : '', 'vas283-c-std') : '',
                    mcellHtml(line.UomName, 'vas283-c-std'),
                    // VAS163 2026-11-11: Row 117 - quantities in the line's selected UOM (server).
                    mcellHtml(isItem ? formatQty(line.QtyOrdered) : '-', 'vas283-c-std', 'right'),
                    mcellHtml(isItem ? formatQty(line.QtyDelivered) : '-', 'vas283-c-std', 'right'),
                    mcellHtml(isItem ? formatQty(line.QtyPending) : '-', 'vas283-c-prim', 'right'),
                    mcellHtml(isItem ? inStockText(line) : '-', (isItem && Number(line.QtyShort) > 0 ? 'vas283-c-short' : 'vas283-c-ok'), 'right'),
                    // VAS163 2026-11-11: Rows 123/124 - ex-tax Rate / Amount in the order's currency.
                    mcellHtml(formatMoney(line.Rate, cur, 2), 'vas283-c-std', 'right'),
                    mcellHtml(formatMoney(line.Amount, cur, prec), 'vas283-c-emph', 'right')
                ].join('') + (st
                    ? '<span class="vas283-mcell"><span class="vas283-mchip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>'
                    : '<span class="vas283-mcell">-</span>');
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
            var el = tableEl(id); // VAS163 2026-11-11: Row 115
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
            var ns = '.vas283-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        /* VAS163 2026-11-11: Row 115 - bug: refresh only re-ran the request. After the
           mosaic / dashboard "refresh" the host can detach the widget's content and strip
           its jQuery handlers (and remove the body-level modal), so the data was rendered
           into detached nodes and the widget stayed empty until a page reload. Now the
           refresh re-attaches the shell (and the root to the frame's content grid),
           re-creates the modal if it was removed, re-binds every handler and then reloads
           the first page of the selected period (same pattern as VAS_270 / VAS_279 /
           VAS_285). */
        this.refreshWidget = function () {
            if (!$mask || !$.contains(document.body, $mask[0])) { createModal(); }
            if ($shell && !$.contains($root[0], $shell[0])) { $root.append($shell); }
            if (!$.contains(document.body, $root[0]) && $self.frame && $self.frame.getContentGrid) {
                try { $self.frame.getContentGrid().append($root); } catch (e) { /* host grid not available */ }
            }
            bindWidgetEvents();
            bindModal();
            loadWarehouseStatus(0);
        };
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
