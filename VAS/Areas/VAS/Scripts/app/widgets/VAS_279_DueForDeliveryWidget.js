/**
 * VAS_279 Due for Delivery Widget (Sales Order dashboard, 2x2 KPI + preview)
 * Purpose - A near-term commitment window: firm (DocStatus 'CO') Sales Orders with
 *           pending delivery, promised for delivery in the CURRENT calendar month -
 *           no Month/Year filter, always recomputed against today. Shows a headline
 *           count, a supporting "value · next-7-days" meta line, and the 3 rows with
 *           the earliest promised dates. An overdue order promised earlier in the
 *           current month stays counted here while pending (this tile answers
 *           "promised in this month", not "not yet overdue" - VAS_274 Overdue
 *           Deliveries is the dedicated overdue view). The next-7-days figure is
 *           calendar days from today through today+7 inclusive and excludes rows
 *           that are already overdue.
 *
 * Design  - 12-due-for-delivery.html / .md: glass 2x2 tile, header (no filter),
 *           KPI headline + meta line directly under the header (not in a .kpi
 *           container), then a 3-row preview list separated by a top divider - no
 *           footer, no pager. Each row is a full-width button: SO number (link
 *           color) over "<customer first word> · <promised date short>" on the
 *           left, order value on the right. Row click opens that order's record
 *           modal DIRECTLY - there is no intermediate documents-list modal for this
 *           widget, and no back button (nothing to go back to).
 *
 * Backend - VAS_279_DueForDeliveryWidget/GetSummary            (GET -> KPI figures + 3-row preview)
 *           VAS_279_DueForDeliveryWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Due for Delivery                                                      | VAS_279_Title
 *  2  | Promised in                                                           | VAS_279_SubtitlePrefix
 *  3  | due in the next 7 days                                                | VAS_279_Next7Suffix
 *  4  | Nothing promised for the rest of                                      | VAS_279_ZeroStateMetaPrefix
 *  5  | No deliveries due                                                     | VAS_279_ZeroStateList
 *  6  | Figures unavailable                                                   | VAS_279_ErrorState
 *  7  | Customer                                                              | VAS_279_Customer
 *  8  | SO date                                                               | VAS_279_SoDate
 *  9  | Date promised                                                         | VAS_279_DatePromised
 * 10  | SO value                                                              | VAS_279_SoValue
 * 11  | Ship from                                                             | VAS_279_ShipFrom
 * 12  | Delivery mode                                                         | VAS_279_DeliveryMode
 * 13  | Document status                                                       | VAS_279_DocumentStatus
 * 14  | Delivery status                                                       | VAS_279_DeliveryStatus
 * 15  | Sales order lines                                                     | VAS_279_SalesOrderLines
 * 16  | Line                                                                  | VAS_279_ColLine
 * 17  | Product                                                               | VAS_279_ColProduct
 * 18  | Attribute                                                             | VAS_279_ColAttribute
 * 19  | UoM                                                                   | VAS_279_ColUom
 * 20  | Ordered                                                               | VAS_279_ColOrdered
 * 21  | Delivered                                                             | VAS_279_ColDelivered
 * 22  | Pending                                                               | VAS_279_ColPending
 * 23  | In stock                                                              | VAS_279_ColInStock
 * 24  | Rate                                                                  | VAS_279_ColRate
 * 25  | Amount                                                                | VAS_279_ColAmount
 * 26  | Line status                                                           | VAS_279_ColLineStatus
 * 27  | Delivered                                                             | VAS_279_DeliveryFull
 * 28  | Partially delivered                                                   | VAS_279_DeliveryPartial
 * 29  | Not delivered                                                         | VAS_279_DeliveryNone
 * 30  | Partly delivered                                                      | VAS_279_LinePartial
 * 31  | In process                                                            | VAS_279_LineInProcess
 * 32  | lines                                                                 | VAS_279_LinesSuffix
 * 33  | qty ordered                                                           | VAS_279_QtyOrderedSuffix
 * 34  | qty short of stock                                                    | VAS_279_QtyShortSuffix
 * 35  | Close                                                                 | VAS_279_Close
 * 36  | Previous page                                                         | VAS_279_PrevPage
 * 37  | Next page                                                             | VAS_279_NextPage
 * 38  | of                                                                    | VAS_279_Of
 * 39  | Showing                                                               | VAS_279_Showing
 * 40  | Search is unavailable right now. Try again in a moment.               | VAS_279_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-278). */
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

    VAS.VAS_279_DueForDeliveryWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas279-root">');
        var $shell, $kpiVal, $kpiMeta, $list;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_279_DueForDeliveryWidget/';

        var summaryState = 'loading'; // 'loading' | 'ready' | 'error'
        var summary = null;

        var lineState = null; // { order, lines, page, size, tableId }

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
            if (name === 'prev') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
            }
            if (name === 'next') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';
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

        function currentMonthLabel() {
            var now = new Date();
            return MONTHS[now.getMonth()] + ' ' + now.getFullYear();
        }

        // Truncating the customer to its first word is deliberate (design.md §9):
        // in a 2-column cell the full name would consume the row, and the date is
        // the more useful half.
        function firstWord(name) {
            var trimmed = String(name || '').trim();
            if (!trimmed) { return ''; }
            var idx = trimmed.indexOf(' ');
            return idx === -1 ? trimmed : trimmed.substring(0, idx);
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas279-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas279-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (no filter) + KPI block + 3-row preview
         * ============================================================ */
        function createWidget() {
            $shell = $('<div class="vas279-shell"></div>');

            var $head = $('<div class="vas279-head"></div>');
            var $htxt = $('<div class="vas279-head-txt"></div>');
            $htxt.append('<p class="vas279-title">' + escapeHtml(label('VAS_279_Title', 'Due for Delivery')) + '</p>');
            $htxt.append('<p class="vas279-sub">' + escapeHtml(label('VAS_279_SubtitlePrefix', 'Promised in') + ' ' + currentMonthLabel()) + '</p>');
            $head.append($htxt);

            $kpiVal = $('<p class="vas279-kpi-val"><span class="vas279-skel-val"></span></p>');
            $kpiMeta = $('<p class="vas279-kpi-meta"></p>');
            $list = $('<div class="vas279-mini"></div>');

            $shell.append($head, $kpiVal, $kpiMeta, $list);
            $root.append($shell);

            $list.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-so]') : null;
                if (row) { openRecordModal(Number(row.getAttribute('data-so'))); }
            });
        }

        function skeletonRows() {
            var rows = '';
            for (var i = 0; i < 3; i++) {
                rows += '<div class="vas279-mrow2 vas279-skel-row"><span class="vas279-mtxt"><span class="vas279-skel-nm"></span><span class="vas279-skel-mt"></span></span></div>';
            }
            return rows;
        }

        function loadSummary() {
            summaryState = 'loading';
            renderWidget();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { summaryState = 'error'; summary = null; }
                    else { summaryState = 'ready'; summary = parsed; }
                    renderWidget();
                },
                error: function () {
                    summaryState = 'error'; summary = null;
                    renderWidget();
                }
            });
        }

        function renderWidget() {
            if (summaryState === 'loading') {
                $kpiVal.html('<span class="vas279-skel-val"></span>');
                $kpiMeta.text('');
                $list.html(skeletonRows());
                return;
            }
            if (summaryState === 'error' || !summary) {
                $kpiVal.removeClass().addClass('vas279-kpi-val error').text('—');
                $kpiMeta.text(label('VAS_279_ErrorState', 'Figures unavailable'));
                $list.html('');
                return;
            }

            $kpiVal.removeClass().addClass('vas279-kpi-val info').text(formatNum(summary.DueThisMonthCount));

            if (summary.DueThisMonthCount <= 0) {
                $kpiMeta.text(label('VAS_279_ZeroStateMetaPrefix', 'Nothing promised for the rest of') + ' ' + currentMonthLabel().split(' ')[0]);
                $list.html('<div class="vas279-empty">' + escapeHtml(label('VAS_279_ZeroStateList', 'No deliveries due')) + '</div>');
                return;
            }

            var meta = formatINR(summary.DueThisMonthValue) + ' · ' + formatNum(summary.DueNext7DaysCount) + ' ' + label('VAS_279_Next7Suffix', 'due in the next 7 days');
            $kpiMeta.attr('title', meta).text(meta);

            var rows = summary.Preview || [];
            $list.html(rows.map(function (row) {
                var meta2 = escapeHtml(firstWord(row.CustomerName)) + ' · ' + escapeHtml(formatDateShort(row.PromisedDate));
                return '<button type="button" class="vas279-mrow2" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' +
                    '<span class="vas279-mtxt">' +
                        '<span class="vas279-mname" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</span>' +
                        '<span class="vas279-mmeta" title="' + escapeHtml(row.CustomerName) + ' · ' + escapeHtml(formatDateFull(row.PromisedDate)) + '">' + meta2 + '</span>' +
                    '</span>' +
                    '<span class="vas279-mval" title="' + escapeHtml(formatINR(row.OrderValue)) + '">' + escapeHtml(formatINR(row.OrderValue)) + '</span></button>';
            }).join(''));
        }

        /* ============================================================
         * Record modal - opened DIRECTLY by a preview row click. There is no
         * parent/documents-list modal for this widget, so the back button never
         * shows and closing always clears to a clean slate.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas279-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas279-modal"></div>');
            $mHead = $('<div class="vas279-mhead"></div>');
            var $htxt = $('<div class="vas279-htxt"></div>');
            $mBack = $('<button type="button" class="vas279-xbtn" hidden></button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas279-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas279-xbtn" aria-label="' + escapeHtml(label('VAS_279_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas279-mbody"></div>');
            $mFoot = $('<div class="vas279-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            $closeBtn.on('click', closeModal);
            $mBody.on('click', function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnLinePage(Number(pageBtn.getAttribute('data-dir'))); }
            });
        }

        function bindDocumentLevelEvents() {
            var ns = '.vas279-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $(window).on('resize' + ns, function () {
                if ($mask.hasClass('is-open')) { fitLineTable(); }
            });
        }

        function closeModal() {
            $mask.removeClass('is-open');
            lineState = null;
        }

        function showLoading() {
            $mTitle.text('…');
            $mSub.text('');
            $mBody.html('<div class="vas279-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas279-mstate">' + escapeHtml(label('VAS_279_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        function lineStatus(line) {
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_279_DeliveryFull', 'Delivered'), cls: 'vas279-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_279_LinePartial', 'Partly delivered'), cls: 'vas279-chip-warn' }; }
            return { text: label('VAS_279_LineInProcess', 'In process'), cls: 'vas279-chip-prop' };
        }

        function openRecordModal(orderId) {
            showLoading();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetSalesOrderDetail',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: orderId },
                success: function (res) {
                    var parsed = parseResponse(res);
                    var order = parsed && parsed.Order;
                    if (parsed.Error || !order || !order.SalesOrderId) { showLoadError(); return; }
                    renderRecordModal(order, parsed.Lines || []);
                },
                error: function () { showLoadError(); }
            });
        }

        function renderRecordModal(order, lines) {
            $mTitle.text(order.SalesOrderNumber);
            $mSub.text([order.CustomerName, formatDateShort(order.SalesOrderDate), order.DocumentStatus].filter(function (p) { return p; }).join(' · '));

            var statsHtml = '<div class="vas279-mstats">' +
                statTile(label('VAS_279_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_279_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_279_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_279_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_279_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_279_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_279_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_279_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';

            var secHtml = '<div class="vas279-msec">' + escapeHtml(label('VAS_279_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(statsHtml + secHtml + '<div class="vas279-mtwrap"><div class="vas279-mtbl" id="vas279-linetbl"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE };
            drawLineTable();

            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_279_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_279_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_279_QtyShortSuffix', 'qty short of stock') : '');

            $mFoot.html('<span class="vas279-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas279-btn" id="vas279-mclose">' + escapeHtml(label('VAS_279_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas279-mclose').on('click', closeModal);

            requestAnimationFrame(function () { fitLineTable(); requestAnimationFrame(fitLineTable); });
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_279_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_279_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_279_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_279_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_279_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_279_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_279_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_279_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_279_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_279_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_279_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable() {
            var el = document.getElementById('vas279-linetbl');
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas279-mrow vas279-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas279-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas279-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas279-c-prim'),
                    cellHtml(line.AttributeText, 'vas279-c-std'),
                    cellHtml(line.UomName, 'vas279-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas279-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas279-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas279-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas279-c-short' : 'vas279-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas279-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas279-c-emph', 'right')
                ].join('') + '<span class="vas279-cell"><span class="vas279-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas279-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_279_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_279_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas279-mtfoot"><span class="vas279-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas279-pager">' +
                        '<button type="button" class="vas279-pbtn" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_279_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas279-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_279_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas279-pbtn" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_279_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas279-mbody-rows">' + body + '</div>' + foot;
        }

        function turnLinePage(dir) {
            if (!lineState) { return; }
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            lineState.page = Math.min(pages - 1, Math.max(0, lineState.page + dir));
            drawLineTable();
        }

        /* Sizes the line table's rows-per-page to the space actually left in the
           modal body, so the body never grows an inner scrollbar. */
        function fitLineTable() {
            if (!$mask.hasClass('is-open') || !lineState) { return; }

            var el = document.getElementById('vas279-linetbl');
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas279-mhead-row');
            var foot = el.querySelector('.vas279-mtfoot');
            var row = el.querySelector('.vas279-mbody-rows .vas279-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== lineState.size) {
                lineState.size = n;
                drawLineTable();
            }
        }

        this.Initalize = function () {
            createWidget();
            createModal();
            bindDocumentLevelEvents();
            loadSummary();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas279-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadSummary(); };
    };

    VAS.VAS_279_DueForDeliveryWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_279_DueForDeliveryWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_279_DueForDeliveryWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_279_DueForDeliveryWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_279_DueForDeliveryWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
