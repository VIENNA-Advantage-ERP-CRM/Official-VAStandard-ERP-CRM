/**
 * VAS_286 SO Pending for Payment Widget (Sales Order dashboard, 6x2 worklist table)
 * Purpose - The collection worklist: Sales Orders with a real outstanding payment
 *           obligation, sorted oldest payment-due-date first so the most delinquent
 *           rows lead. Header chip totals the amount due across the COMPLETE result
 *           (not just the visible page) and flips to the ok tone the moment nothing
 *           is outstanding.
 *
 *           CRITICAL USER OVERRIDE (19_SO_Pending_For_Payment_Claude_Development_
 *           Prompt.txt): the paired mock's "Delivered against SO, payment not yet
 *           received" framing is NOT the production rule - delivery is never
 *           required. A Sales Order qualifies either because (A) it carries one or
 *           more open completed/closed sales invoices (VA009_OpenAmount > 0 - amount
 *           = summed open amount, due date = earliest invoice DueDate), or (B) no
 *           qualifying invoice exists but its payment term is an advance term
 *           (C_PaymentTerm.VA009_Advance='Y') and GrandTotal less completed linked
 *           receipts is still positive (amount = that difference, due date = the
 *           order date itself - advance is due from booking). A single SO never
 *           carries both rows - invoice basis always wins when it applies.
 *
 * Design  - 19-so-pending-for-payment.html / .md: glass 6x2 tile, header (title +
 *           the overridden subtitle) + a `.chip-risk` "X due" in the right slot, a
 *           CSS-grid data table (7 columns, header and body sharing one identical
 *           grid-template-columns string) with 6 rows per page, and a footer pager.
 *           Per the prompt's UI overrides, the mock's "Delivered on" column is
 *           replaced by "Payment type" (Invoice / Advance); the payment-due cell is
 *           conditional - a plain short date when not yet due, or a `.chip-risk`
 *           reading "Overdue Nd" when it is (never both). The entire row (not just
 *           the SO number) opens that order's record modal DIRECTLY - there is no
 *           intermediate list, matching the shared record/lines modal used by every
 *           other widget on this dashboard.
 *
 * Backend - VAS_286_SOPendingForPaymentWidget/GetPendingPayment    (GET page,size -> ranked page + total due)
 *           VAS_286_SOPendingForPaymentWidget/GetSalesOrderDetail  (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | SO Pending for Payment                                                | VAS_286_Title
 *  2  | Outstanding payment against Sales Orders                              | VAS_286_Subtitle
 *  3  | due                                                                   | VAS_286_DueSuffix
 *  4  | All settled                                                           | VAS_286_AllSettledChip
 *  5  | No sales orders awaiting payment                                      | VAS_286_ZeroState
 *  6  | Payment position unavailable                                          | VAS_286_ErrorState
 *  7  | SO No                                                                 | VAS_286_ColSoNo
 *  8  | SO date                                                               | VAS_286_ColSoDate
 *  9  | Customer                                                              | VAS_286_ColCustomer
 * 10  | Ship from                                                             | VAS_286_ColShipFrom
 * 11  | Payment type                                                          | VAS_286_ColPaymentType
 * 12  | Payment due                                                           | VAS_286_ColPaymentDue
 * 13  | Amount                                                                | VAS_286_ColAmount
 * 14  | Invoice                                                               | VAS_286_TypeInvoice
 * 15  | Advance                                                               | VAS_286_TypeAdvance
 * 16  | Overdue                                                               | VAS_286_OverduePrefix
 * 17  | oldest due first · click a row for the SO                             | VAS_286_RankedBy
 * 18  | Customer                                                              | VAS_286_Customer
 * 19  | SO date                                                               | VAS_286_SoDate
 * 20  | Date promised                                                         | VAS_286_DatePromised
 * 21  | SO value                                                              | VAS_286_SoValue
 * 22  | Ship from                                                             | VAS_286_ShipFrom
 * 23  | Delivery mode                                                         | VAS_286_DeliveryMode
 * 24  | Document status                                                       | VAS_286_DocumentStatus
 * 25  | Delivery status                                                       | VAS_286_DeliveryStatus
 * 26  | Sales order lines                                                     | VAS_286_SalesOrderLines
 * 27  | Line                                                                  | VAS_286_ColLine
 * 28  | Product                                                               | VAS_286_ColProduct
 * 29  | Attribute                                                             | VAS_286_ColAttribute
 * 30  | UoM                                                                   | VAS_286_ColUom
 * 31  | Ordered                                                               | VAS_286_ColOrdered
 * 32  | Delivered                                                             | VAS_286_ColDelivered
 * 33  | Pending                                                               | VAS_286_ColPending
 * 34  | In stock                                                              | VAS_286_ColInStock
 * 35  | Rate                                                                  | VAS_286_ColRate
 * 36  | Amount                                                                | VAS_286_ColLineAmount
 * 37  | Line status                                                           | VAS_286_ColLineStatus
 * 38  | Delivered                                                             | VAS_286_DeliveryFull
 * 39  | Partially delivered                                                   | VAS_286_DeliveryPartial
 * 40  | Not delivered                                                         | VAS_286_DeliveryNone
 * 41  | Partly delivered                                                      | VAS_286_LinePartial
 * 42  | In process                                                            | VAS_286_LineInProcess
 * 43  | lines                                                                 | VAS_286_LinesSuffix
 * 44  | qty ordered                                                           | VAS_286_QtyOrderedSuffix
 * 45  | qty short of stock                                                    | VAS_286_QtyShortSuffix
 * 46  | Close                                                                 | VAS_286_Close
 * 47  | Previous page                                                         | VAS_286_PrevPage
 * 48  | Next page                                                             | VAS_286_NextPage
 * 49  | of                                                                    | VAS_286_Of
 * 50  | Showing                                                               | VAS_286_Showing
 * 51  | Search is unavailable right now. Try again in a moment.               | VAS_286_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var WIDGET_PAGE_SIZE = 6;   // rows per widget page
    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    // Header row + every body row share this exact grid-template-columns string
    // (design.md §11, with the mock's "Delivered on" column replaced by "Payment
    // type" per the prompt's UI override): SO No / SO date / Customer / Ship from /
    // Payment type / Payment due / Amount.
    var TABLE_TEMPLATE = 'minmax(0,1.1fr) minmax(0,.95fr) minmax(0,1.7fr) minmax(0,1.1fr) minmax(0,.95fr) minmax(0,1fr) minmax(0,1fr)';

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the widget clamp resolves against the dashboard's visible
       width, not the viewport. A single document-level ResizeObserver serves every
       widget (matches VAS_269-285). */
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

    VAS.VAS_286_SOPendingForPaymentWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas286-root">');
        var $shell, $chip, $tbody, $helper, $pageTxt, $prevBtn, $nextBtn;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_286_SOPendingForPaymentWidget/';

        var listState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: WIDGET_PAGE_SIZE, total: 0, rows: [], totalDue: 0 };

        var lineState = null; // { order, lines, page, size }

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

        function paymentTypeLabel(code) {
            if (code === 'Advance') { return label('VAS_286_TypeAdvance', 'Advance'); }
            return label('VAS_286_TypeInvoice', 'Invoice');
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas286-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas286-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (title/subtitle + chip) + worklist table + pager
         * ============================================================ */
        function createWidget() {
            $shell = $('<div class="vas286-shell"></div>');

            var $head = $('<div class="vas286-head"></div>');
            var $htxt = $('<div class="vas286-head-txt"></div>');
            $htxt.append('<p class="vas286-title">' + escapeHtml(label('VAS_286_Title', 'SO Pending for Payment')) + '</p>');
            $htxt.append('<p class="vas286-sub">' + escapeHtml(label('VAS_286_Subtitle', 'Outstanding payment against Sales Orders')) + '</p>');
            $chip = $('<span class="vas286-chip vas286-chip-risk"></span>');
            $head.append($htxt, $chip);

            var $tbl = $('<div class="vas286-tbl"></div>');
            var $thead = $('<div class="vas286-trow vas286-thead"></div>').css('grid-template-columns', TABLE_TEMPLATE);
            $thead.html([
                { l: label('VAS_286_ColSoNo', 'SO No'), right: false },
                { l: label('VAS_286_ColSoDate', 'SO date'), right: false },
                { l: label('VAS_286_ColCustomer', 'Customer'), right: false },
                { l: label('VAS_286_ColShipFrom', 'Ship from'), right: false },
                { l: label('VAS_286_ColPaymentType', 'Payment type'), right: false },
                { l: label('VAS_286_ColPaymentDue', 'Payment due'), right: false },
                { l: label('VAS_286_ColAmount', 'Amount'), right: true }
            ].map(function (c) {
                return '<span class="vas286-cell' + (c.right ? ' right' : '') + '" title="' + escapeHtml(c.l) + '">' + escapeHtml(c.l) + '</span>';
            }).join(''));

            $tbody = $('<div class="vas286-tbody"></div>');
            $tbl.append($thead, $tbody);

            var $foot = $('<div class="vas286-wfoot"></div>');
            $helper = $('<span class="vas286-helper"></span>');
            var $pager = $('<div class="vas286-pager"></div>');
            $prevBtn = $('<button type="button" class="vas286-pbtn" aria-label="' + escapeHtml(label('VAS_286_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>');
            $pageTxt = $('<span class="vas286-ptxt"></span>');
            $nextBtn = $('<button type="button" class="vas286-pbtn" aria-label="' + escapeHtml(label('VAS_286_NextPage', 'Next page')) + '">' + icon('next') + '</button>');
            $pager.append($prevBtn, $pageTxt, $nextBtn);
            $foot.append($helper, $pager);

            $shell.append($head, $tbl, $foot);
            $root.append($shell);

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
                rows += '<div class="vas286-trow" style="grid-template-columns:' + tpl + ';cursor:default">' +
                    '<span class="vas286-cell"><span class="vas286-skel-cell" style="width:70%"></span></span>' +
                    '<span class="vas286-cell"><span class="vas286-skel-cell" style="width:55%"></span></span>' +
                    '<span class="vas286-cell"><span class="vas286-skel-cell" style="width:75%"></span></span>' +
                    '<span class="vas286-cell"><span class="vas286-skel-cell" style="width:60%"></span></span>' +
                    '<span class="vas286-cell"><span class="vas286-skel-cell" style="width:50%"></span></span>' +
                    '<span class="vas286-cell"><span class="vas286-skel-cell" style="width:55%"></span></span>' +
                    '<span class="vas286-cell right"><span class="vas286-skel-cell" style="width:50%;margin-left:auto"></span></span>' +
                '</div>';
            }
            return rows;
        }

        function loadPendingPayment(page) {
            listState = 'loading';
            renderWidget();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetPendingPayment',
                type: 'GET', dataType: 'json', cache: false,
                data: { page: page, size: WIDGET_PAGE_SIZE },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { listState = 'error'; }
                    else {
                        listState = 'ready';
                        docsState.page = page;
                        docsState.total = Number(parsed.Total || 0);
                        docsState.totalDue = Number(parsed.TotalDue || 0);
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
                $chip.hide();
                $tbody.html(skeletonRows());
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }
            if (listState === 'error') {
                $chip.hide();
                $tbody.html('<div class="vas286-empty">' + escapeHtml(label('VAS_286_ErrorState', 'Payment position unavailable')) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            $chip.show();
            if (docsState.total <= 0) {
                $chip.removeClass('vas286-chip-risk').addClass('vas286-chip-ok').text(label('VAS_286_AllSettledChip', 'All settled'));
                $tbody.html('<div class="vas286-empty">' + escapeHtml(label('VAS_286_ZeroState', 'No sales orders awaiting payment')) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            var chipText = formatINR(docsState.totalDue) + ' ' + label('VAS_286_DueSuffix', 'due');
            $chip.removeClass('vas286-chip-ok').addClass('vas286-chip-risk').attr('title', chipText).text(chipText);

            var tpl = TABLE_TEMPLATE;
            $tbody.html(docsState.rows.map(function (row) {
                var dueCell;
                if (row.IsOverdue) {
                    var overdueText = label('VAS_286_OverduePrefix', 'Overdue') + ' ' + row.DaysOverdue + 'd';
                    dueCell = '<span class="vas286-cell" title="' + escapeHtml(overdueText) + '"><span class="vas286-chip vas286-chip-risk">' + escapeHtml(overdueText) + '</span></span>';
                } else {
                    dueCell = cellHtml(formatDateShort(row.PaymentDueDate), 'vas286-c-std');
                }
                return '<button type="button" class="vas286-trow" style="grid-template-columns:' + tpl + '" data-so="' + row.SalesOrderId + '">' +
                    '<span class="vas286-cell vas286-c-link" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</span>' +
                    cellHtml(formatDateShort(row.SalesOrderDate), 'vas286-c-std') +
                    cellHtml(row.CustomerName, 'vas286-c-std') +
                    cellHtml(row.WarehouseName || '—', 'vas286-c-dark') +
                    cellHtml(paymentTypeLabel(row.PaymentType), 'vas286-c-std') +
                    dueCell +
                    cellHtml(formatINR(row.Amount), 'vas286-c-emph', 'right') +
                '</button>';
            }).join(''));

            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var start = docsState.page * docsState.size;
            var helperText = label('VAS_286_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_286_Of', 'of') + ' ' + docsState.total + ' · ' + label('VAS_286_RankedBy', 'oldest due first · click a row for the SO');
            $helper.attr('title', helperText).text(helperText);
            $pageTxt.text((docsState.page + 1) + ' ' + label('VAS_286_Of', 'of') + ' ' + pages);
            $prevBtn.prop('disabled', docsState.page === 0);
            $nextBtn.prop('disabled', docsState.page >= pages - 1);
        }

        function turnWidgetPage(dir) {
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var next = Math.min(pages - 1, Math.max(0, docsState.page + dir));
            if (next === docsState.page) { return; }
            loadPendingPayment(next);
        }

        /* ============================================================
         * Record modal - opened DIRECTLY by a row click. There is no parent/
         * documents-list modal for this widget (design.md §12: "there is no
         * intermediate list"), so the back button never shows.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas286-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas286-modal"></div>');
            $mHead = $('<div class="vas286-mhead"></div>');
            var $htxt = $('<div class="vas286-htxt"></div>');
            $mBack = $('<button type="button" class="vas286-xbtn" hidden></button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas286-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas286-xbtn" aria-label="' + escapeHtml(label('VAS_286_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas286-mbody"></div>');
            $mFoot = $('<div class="vas286-mfoot"></div>');

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
            var ns = '.vas286-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            $mBody.html('<div class="vas286-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas286-mstate">' + escapeHtml(label('VAS_286_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        function lineStatus(line) {
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_286_DeliveryFull', 'Delivered'), cls: 'vas286-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_286_LinePartial', 'Partly delivered'), cls: 'vas286-chip-warn' }; }
            return { text: label('VAS_286_LineInProcess', 'In process'), cls: 'vas286-chip-neutral' };
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

            var statsHtml = '<div class="vas286-mstats">' +
                statTile(label('VAS_286_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_286_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_286_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_286_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_286_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_286_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_286_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_286_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';

            var secHtml = '<div class="vas286-msec">' + escapeHtml(label('VAS_286_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(statsHtml + secHtml + '<div class="vas286-mtwrap"><div class="vas286-mtbl" id="vas286-linetbl"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE };
            drawLineTable();

            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_286_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_286_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_286_QtyShortSuffix', 'qty short of stock') : '');

            $mFoot.html('<span class="vas286-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas286-btn" id="vas286-mclose">' + escapeHtml(label('VAS_286_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas286-mclose').on('click', closeModal);

            requestAnimationFrame(function () { fitLineTable(); requestAnimationFrame(fitLineTable); });
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_286_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_286_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_286_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_286_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_286_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_286_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_286_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_286_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_286_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_286_ColLineAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_286_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable() {
            var el = document.getElementById('vas286-linetbl');
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas286-mrow vas286-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas286-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas286-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas286-c-prim'),
                    cellHtml(line.AttributeText, 'vas286-c-std'),
                    cellHtml(line.UomName, 'vas286-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas286-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas286-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas286-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas286-c-short' : 'vas286-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas286-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas286-c-emph', 'right')
                ].join('') + '<span class="vas286-cell"><span class="vas286-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas286-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_286_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_286_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas286-mtfoot"><span class="vas286-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas286-pager">' +
                        '<button type="button" class="vas286-pbtn" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_286_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas286-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_286_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas286-pbtn" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_286_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas286-mbody-rows">' + body + '</div>' + foot;
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

            var el = document.getElementById('vas286-linetbl');
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas286-mhead-row');
            var foot = el.querySelector('.vas286-mtfoot');
            var row = el.querySelector('.vas286-mbody-rows .vas286-mrow');
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
            loadPendingPayment(0);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas286-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadPendingPayment(docsState.page); };
    };

    VAS.VAS_286_SOPendingForPaymentWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_286_SOPendingForPaymentWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_286_SOPendingForPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_286_SOPendingForPaymentWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_286_SOPendingForPaymentWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
