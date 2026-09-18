/**
 * VAS_288 SO Queue Widget (Sales Order dashboard, 6x3 primary operational work queue)
 * Purpose - The dashboard's main working list: live Sales Orders for a selected
 *           promised-delivery Month/Year, sorted by DatePromised ascending (the
 *           next thing due always leads page 1). Live = IsActive='Y', IsSOTrx='Y',
 *           non-return, non-quotation, DocStatus IN ('DR','IP','CO') only - Closed,
 *           Voided and Reversed are excluded entirely since this is an operational
 *           queue, not a value report. Six rows per page.
 *
 *           UI FIELD NAMES (21_SO_Queue_Claude_Development_Prompt.txt CONFIRMED):
 *           table headers use exact schema terminology (Document No. / Date Ordered
 *           / Business Partner / Warehouse / Quotation No. / Sales Rep / Date
 *           Promised / SubTotal / Document Status), never the generic mock labels.
 *           The Document Status column shows the RAW DocStatus vocabulary only
 *           (Drafted / In Progress / Completed) - never a delivery-progress label;
 *           delivery progress is a different concept that still lives inside the
 *           shared record modal's own "Delivery status" stat card. Quotation No.
 *           comes from C_Order.C_Order_Quotation -&gt; the source quotation's own
 *           DocumentNo, rendering an em dash for a direct order with none.
 *
 * Design  - 21-so-queue.html / .md: glass 6x3 tile, header with title + subtitle +
 *           a Month/Year period filter (stopPropagation so it never also triggers
 *           a row's click handler), a CSS-grid data table (9 columns, header and
 *           body sharing one identical grid-template-columns string) with 7 rows
 *           per page, and a footer pager. The whole row (not just Document No.) is
 *           the click target and opens that order's shared record-preview modal
 *           DIRECTLY - there is no intermediate list, matching VAS_286's pattern
 *           (no back button, since there is nothing to go back to at the top level).
 *
 * Backend - VAS_288_SOQueueWidget/GetQueue            (GET month,year,page,size -> paginated live queue)
 *           VAS_288_SOQueueWidget/GetSalesOrderDetail (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | SO Queue                                                              | VAS_288_Title
 *  2  | Live sales orders by promised delivery date                          | VAS_288_Subtitle
 *  3  | click a row to open the SO                                           | VAS_288_ClickRowHint
 *  4  | No live Sales Orders promised in                                     | VAS_288_ZeroStatePrefix
 *  5  | SO Queue unavailable                                                 | VAS_288_ErrorState
 *  6  | Document No.                                                         | VAS_288_ColDocumentNo
 *  7  | Date Ordered                                                         | VAS_288_ColDateOrdered
 *  8  | Business Partner                                                     | VAS_288_ColBusinessPartner
 *  9  | Warehouse                                                            | VAS_288_ColWarehouse
 * 10  | Quotation No.                                                        | VAS_288_ColQuotationNo
 * 11  | Sales Rep                                                            | VAS_288_ColSalesRep
 * 12  | Date Promised                                                        | VAS_288_ColDatePromised
 * 13  | SubTotal                                                             | VAS_288_ColSubTotal
 * 14  | Document Status                                                      | VAS_288_ColDocumentStatus
 * 15  | Drafted                                                              | VAS_288_StatusDrafted
 * 16  | In Progress                                                          | VAS_288_StatusInProgress
 * 17  | Completed                                                            | VAS_288_StatusCompleted
 * 18  | Back                                                                 | VAS_288_Back
 * 19  | Close                                                                | VAS_288_Close
 * 20  | Customer                                                             | VAS_288_Customer
 * 21  | SO date                                                              | VAS_288_SoDate
 * 22  | Date promised                                                        | VAS_288_DatePromised
 * 23  | SO value                                                             | VAS_288_SoValue
 * 24  | Ship from                                                            | VAS_288_ShipFrom
 * 25  | Delivery mode                                                        | VAS_288_DeliveryMode
 * 26  | Document status                                                      | VAS_288_DocumentStatus
 * 27  | Delivery status                                                      | VAS_288_DeliveryStatus
 * 28  | Sales order lines                                                    | VAS_288_SalesOrderLines
 * 29  | Line                                                                 | VAS_288_ColLine
 * 30  | Product                                                              | VAS_288_ColProduct
 * 31  | Attribute                                                            | VAS_288_ColAttribute
 * 32  | UoM                                                                  | VAS_288_ColUom
 * 33  | Ordered                                                              | VAS_288_ColOrdered
 * 34  | Delivered                                                            | VAS_288_ColDelivered
 * 35  | Pending                                                              | VAS_288_ColPending
 * 36  | In stock                                                             | VAS_288_ColInStock
 * 37  | Rate                                                                 | VAS_288_ColRate
 * 38  | Amount                                                               | VAS_288_ColLineAmount
 * 39  | Line status                                                          | VAS_288_ColLineStatus
 * 40  | Delivered                                                            | VAS_288_DeliveryFull
 * 41  | Partially delivered                                                  | VAS_288_DeliveryPartial
 * 42  | Not delivered                                                        | VAS_288_DeliveryNone
 * 43  | Partly delivered                                                     | VAS_288_LinePartial
 * 44  | In process                                                           | VAS_288_LineInProcess
 * 45  | lines                                                                | VAS_288_LinesSuffix
 * 46  | qty ordered                                                          | VAS_288_QtyOrderedSuffix
 * 47  | qty short of stock                                                   | VAS_288_QtyShortSuffix
 * 48  | Previous page                                                        | VAS_288_PrevPage
 * 49  | Next page                                                            | VAS_288_NextPage
 * 50  | of                                                                   | VAS_288_Of
 * 51  | Showing                                                              | VAS_288_Showing
 * 52  | Search is unavailable right now. Try again in a moment.              | VAS_288_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var WIDGET_PAGE_SIZE = 6;   // rows per widget page
    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MIN_YEAR = 2024;
    var YEAR_SPAN = 3;

    // Header row + every body row share this exact grid-template-columns string
    // (design.md §11, preserved from the mock): Document No. / Date Ordered /
    // Business Partner / Warehouse / Quotation No. / Sales Rep / Date Promised /
    // SubTotal / Document Status.
    var TABLE_TEMPLATE = 'minmax(0,1.1fr) minmax(0,.8fr) minmax(0,1.6fr) minmax(0,1.1fr) minmax(0,.9fr) minmax(0,1.2fr) minmax(0,.85fr) minmax(0,.9fr) minmax(0,1.1fr)';

    // Only DR/IP/CO ever appear in this widget's own query - the exact confirmed
    // mapping, never the generic delivery-progress vocabulary used elsewhere.
    var STATUS_META = {
        DR: { fallback: 'Drafted', cls: 'vas288-chip-neutral' },
        IP: { fallback: 'In Progress', cls: 'vas288-chip-prop' },
        CO: { fallback: 'Completed', cls: 'vas288-chip-ok' }
    };

    var now = new Date();
    var CUR_M = now.getMonth();
    var CUR_Y = now.getFullYear();

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the widget clamp resolves against the dashboard's visible
       width, not the viewport. A single document-level ResizeObserver serves every
       widget (matches VAS_269-287). */
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

    VAS.VAS_288_SOQueueWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas288-root">');
        var $shell, $tbody, $helper, $pageTxt, $prevBtn, $nextBtn, $monthSel, $yearSel;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_288_SOQueueWidget/';

        var listState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: WIDGET_PAGE_SIZE, total: 0, rows: [] };

        var lineState = null; // { order, lines, page, size }

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
            var display = (value === null || value === undefined || value === '') ? '—' : value;
            return '<span class="vas288-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value == null ? '' : value) + '">' + escapeHtml(display) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas288-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (title/subtitle + Month/Year filter) + queue table + pager
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
            $shell = $('<div class="vas288-shell"></div>');

            var $head = $('<div class="vas288-head"></div>');
            var $htxt = $('<div class="vas288-head-txt"></div>');
            $htxt.append('<p class="vas288-title">' + escapeHtml(label('VAS_288_Title', 'SO Queue')) + '</p>');
            $htxt.append('<p class="vas288-sub">' + escapeHtml(label('VAS_288_Subtitle', 'Live sales orders by promised delivery date')) + '</p>');

            var $filter = $('<div class="vas288-mfilter"></div>');
            $monthSel = $('<select class="vas288-msel" aria-label="Month"></select>');
            $yearSel = $('<select class="vas288-msel" aria-label="Year"></select>');
            fillMonthSelect($monthSel);
            fillYearSelect($yearSel);
            $filter.append($monthSel, $yearSel);

            $head.append($htxt, $filter);

            var $tbl = $('<div class="vas288-tbl"></div>');
            var $thead = $('<div class="vas288-trow vas288-thead"></div>').css('grid-template-columns', TABLE_TEMPLATE);
            $thead.html([
                { l: label('VAS_288_ColDocumentNo', 'Document No.'), right: false },
                { l: label('VAS_288_ColDateOrdered', 'Date Ordered'), right: false },
                { l: label('VAS_288_ColBusinessPartner', 'Business Partner'), right: false },
                { l: label('VAS_288_ColWarehouse', 'Warehouse'), right: false },
                { l: label('VAS_288_ColQuotationNo', 'Quotation No.'), right: false },
                { l: label('VAS_288_ColSalesRep', 'Sales Rep'), right: false },
                { l: label('VAS_288_ColDatePromised', 'Date Promised'), right: false },
                { l: label('VAS_288_ColSubTotal', 'SubTotal'), right: true },
                { l: label('VAS_288_ColDocumentStatus', 'Document Status'), right: false }
            ].map(function (c) {
                return '<span class="vas288-cell' + (c.right ? ' right' : '') + '" title="' + escapeHtml(c.l) + '">' + escapeHtml(c.l) + '</span>';
            }).join(''));

            $tbody = $('<div class="vas288-tbody"></div>');
            $tbl.append($thead, $tbody);

            var $foot = $('<div class="vas288-wfoot"></div>');
            $helper = $('<span class="vas288-helper"></span>');
            var $pager = $('<div class="vas288-pager"></div>');
            $prevBtn = $('<button type="button" class="vas288-pbtn" aria-label="' + escapeHtml(label('VAS_288_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>');
            $pageTxt = $('<span class="vas288-ptxt"></span>');
            $nextBtn = $('<button type="button" class="vas288-pbtn" aria-label="' + escapeHtml(label('VAS_288_NextPage', 'Next page')) + '">' + icon('next') + '</button>');
            $pager.append($prevBtn, $pageTxt, $nextBtn);
            $foot.append($helper, $pager);

            $shell.append($head, $tbl, $foot);
            $root.append($shell);

            $monthSel.on('click', function (event) { event.stopPropagation(); });
            $yearSel.on('click', function (event) { event.stopPropagation(); });
            $monthSel.on('change', function () { loadQueue(0); });
            $yearSel.on('change', function () { loadQueue(0); });

            $prevBtn.on('click', function () { turnWidgetPage(-1); });
            $nextBtn.on('click', function () { turnWidgetPage(1); });

            $tbody.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-so]') : null;
                if (row) { openRecordModal(Number(row.getAttribute('data-so'))); }
            });
        }

        function skeletonRows() {
            var tpl = TABLE_TEMPLATE;
            var rows = '';
            for (var i = 0; i < WIDGET_PAGE_SIZE; i++) {
                rows += '<div class="vas288-trow" style="grid-template-columns:' + tpl + ';cursor:default">' +
                    '<span class="vas288-cell"><span class="vas288-skel-cell" style="width:70%"></span></span>' +
                    '<span class="vas288-cell"><span class="vas288-skel-cell" style="width:55%"></span></span>' +
                    '<span class="vas288-cell"><span class="vas288-skel-cell" style="width:80%"></span></span>' +
                    '<span class="vas288-cell"><span class="vas288-skel-cell" style="width:60%"></span></span>' +
                    '<span class="vas288-cell"><span class="vas288-skel-cell" style="width:50%"></span></span>' +
                    '<span class="vas288-cell"><span class="vas288-skel-cell" style="width:65%"></span></span>' +
                    '<span class="vas288-cell"><span class="vas288-skel-cell" style="width:55%"></span></span>' +
                    '<span class="vas288-cell right"><span class="vas288-skel-cell" style="width:50%;margin-left:auto"></span></span>' +
                    '<span class="vas288-cell"><span class="vas288-skel-cell" style="width:45%"></span></span>' +
                '</div>';
            }
            return rows;
        }

        function loadQueue(page) {
            listState = 'loading';
            renderWidget();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetQueue',
                type: 'GET', dataType: 'json', cache: false,
                data: { month: selectedMonth(), year: selectedYear(), page: page, size: WIDGET_PAGE_SIZE },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { listState = 'error'; }
                    else {
                        listState = 'ready';
                        docsState.page = page;
                        docsState.total = Number(parsed.Total || 0);
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
                $tbody.html('<div class="vas288-empty">' + escapeHtml(label('VAS_288_ErrorState', 'SO Queue unavailable')) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            if (docsState.total <= 0) {
                $tbody.html('<div class="vas288-empty">' + escapeHtml(label('VAS_288_ZeroStatePrefix', 'No live Sales Orders promised in') + ' ' + periodLabel()) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            var tpl = TABLE_TEMPLATE;
            $tbody.html(docsState.rows.map(function (row) {
                var st = STATUS_META[row.DocumentStatus] || { fallback: row.DocumentStatus, cls: 'vas288-chip-neutral' };
                var statusLabel = label('VAS_288_Status' + (row.DocumentStatus === 'DR' ? 'Drafted' : row.DocumentStatus === 'IP' ? 'InProgress' : 'Completed'), st.fallback);
                return '<button type="button" class="vas288-trow" style="grid-template-columns:' + tpl + '" data-so="' + row.SalesOrderId + '">' +
                    '<span class="vas288-cell vas288-c-link" title="' + escapeHtml(row.DocumentNo) + '">' + escapeHtml(row.DocumentNo) + '</span>' +
                    cellHtml(formatDateShort(row.DateOrdered), 'vas288-c-std') +
                    cellHtml(row.BusinessPartner, 'vas288-c-std') +
                    cellHtml(row.Warehouse || '—', 'vas288-c-dark') +
                    cellHtml(row.QuotationNo, 'vas288-c-std') +
                    cellHtml(row.SalesRep || '—', 'vas288-c-dark') +
                    cellHtml(formatDateShort(row.DatePromised), 'vas288-c-std') +
                    cellHtml(formatINR(row.SubTotal), 'vas288-c-emph', 'right') +
                    '<span class="vas288-cell" title="' + escapeHtml(statusLabel) + '"><span class="vas288-chip ' + st.cls + '">' + escapeHtml(statusLabel) + '</span></span>' +
                '</button>';
            }).join(''));

            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var start = docsState.page * docsState.size;
            var helperText = label('VAS_288_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_288_Of', 'of') + ' ' + docsState.total + ' · ' + label('VAS_288_ClickRowHint', 'click a row to open the SO');
            $helper.attr('title', helperText).text(helperText);
            $pageTxt.text((docsState.page + 1) + ' ' + label('VAS_288_Of', 'of') + ' ' + pages);
            $prevBtn.prop('disabled', docsState.page === 0);
            $nextBtn.prop('disabled', docsState.page >= pages - 1);
        }

        function turnWidgetPage(dir) {
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var next = Math.min(pages - 1, Math.max(0, docsState.page + dir));
            if (next === docsState.page) { return; }
            loadQueue(next);
        }

        /* ============================================================
         * Record modal - opened DIRECTLY by a row click. There is no parent/
         * documents-list modal for this widget, so the back button never shows.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas288-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas288-modal"></div>');
            $mHead = $('<div class="vas288-mhead"></div>');
            var $htxt = $('<div class="vas288-htxt"></div>');
            $mBack = $('<button type="button" class="vas288-xbtn" hidden></button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas288-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas288-xbtn" aria-label="' + escapeHtml(label('VAS_288_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas288-mbody"></div>');
            $mFoot = $('<div class="vas288-mfoot"></div>');

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
            var ns = '.vas288-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            $mBody.html('<div class="vas288-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas288-mstate">' + escapeHtml(label('VAS_288_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        function lineStatus(line) {
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_288_DeliveryFull', 'Delivered'), cls: 'vas288-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_288_LinePartial', 'Partly delivered'), cls: 'vas288-chip-warn' }; }
            return { text: label('VAS_288_LineInProcess', 'In process'), cls: 'vas288-chip-neutral' };
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

            var statsHtml = '<div class="vas288-mstats">' +
                statTile(label('VAS_288_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_288_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_288_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_288_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_288_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_288_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_288_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_288_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';

            var secHtml = '<div class="vas288-msec">' + escapeHtml(label('VAS_288_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(statsHtml + secHtml + '<div class="vas288-mtwrap"><div class="vas288-mtbl" id="vas288-linetbl"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE };
            drawLineTable();

            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_288_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_288_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_288_QtyShortSuffix', 'qty short of stock') : '');

            $mFoot.html('<span class="vas288-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas288-btn" id="vas288-mclose">' + escapeHtml(label('VAS_288_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas288-mclose').on('click', closeModal);

            requestAnimationFrame(function () { fitLineTable(); requestAnimationFrame(fitLineTable); });
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_288_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_288_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_288_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_288_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_288_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_288_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_288_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_288_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_288_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_288_ColLineAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_288_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable() {
            var el = document.getElementById('vas288-linetbl');
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas288-mrow vas288-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas288-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas288-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas288-c-prim'),
                    cellHtml(line.AttributeText, 'vas288-c-std'),
                    cellHtml(line.UomName, 'vas288-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas288-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas288-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas288-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas288-c-short' : 'vas288-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas288-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas288-c-emph', 'right')
                ].join('') + '<span class="vas288-cell"><span class="vas288-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas288-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_288_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_288_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas288-mtfoot"><span class="vas288-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas288-pager">' +
                        '<button type="button" class="vas288-pbtn" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_288_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas288-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_288_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas288-pbtn" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_288_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas288-mbody-rows">' + body + '</div>' + foot;
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

            var el = document.getElementById('vas288-linetbl');
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas288-mhead-row');
            var foot = el.querySelector('.vas288-mtfoot');
            var row = el.querySelector('.vas288-mbody-rows .vas288-mrow');
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
            loadQueue(0);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas288-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadQueue(docsState.page); };
    };

    VAS.VAS_288_SOQueueWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_288_SOQueueWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_288_SOQueueWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_288_SOQueueWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_288_SOQueueWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
