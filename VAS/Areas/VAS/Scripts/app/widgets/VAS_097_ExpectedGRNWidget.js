/**
 * Expected GRN Widget (Material Receipt / GRN dashboard)
 * Purpose - 3x2 glass document list of completed vendor purchase orders whose
 *           promised (expected) date is today or later and that still have open
 *           quantity to receive. Each row shows PO number, supplier, ship-to
 *           address, destination warehouse, line count and PO value. A row click
 *           opens the shared GRN line-entry modal (Item | PO Qty | Received
 *           editable input, defaulting to open qty) whose "Create GRN"
 *           button creates and completes the receipt.
 * Backend - VAS_097_ExpectedGRNWidget/GetExpectedPurchaseOrders
 *           VAS_097_ExpectedGRNWidget/GetPurchaseOrderLines
 *           VAS_097_ExpectedGRNWidget/CreateGRN
 * Summary Message Table: see Labels / Message Keys below.
 *
 * Labels / Message Keys
 *  #  | Current Text                                     | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Expected GRN                                      | VAS_ExpectedGRN
 *  2  | GRN Count                                         | VAS_GRNCount
 *  3  | No of Lines                                       | VAS_NoOfLines
 *  4  | Showing                                           | VAS_Showing
 *  5  | of                                                | VAS_Of
 *  6  | No data available                                | VAS_NoDataAvailable
 *  7  | Back                                              | VAS_Back
 *  8  | Close                                             | VAS_Close
 *  9  | Item                                              | VAS_Item
 * 10  | PO Qty                                            | VAS_POQty
 * 11  | Received                                          | VAS_Received
 * 12  | UOM                                               | VAS_Uom
 * 13  | Enter received quantity against each PO line...   | VAS_EnterReceivedQtyAgainstLine
 * 14  | Create GRN                                       | VAS_097_CreateGRN
 * 15  | Received quantity cannot be negative.             | VAS_NegativeReceivedQty
 * 16  | Enter received quantity for at least one line.    | VAS_ReceivedQtyRequired
 * 17  | Received quantity cannot be greater than order... | VAS_ReceivedQtyTooHigh
 * 18  | GRN could not be created.                         | VAS_GRNCouldNotBeCreated
 * 19  | Attribute                                         | VAS_Attribute
 * 20  | Purchase Order (modal document field)             | PurchaseOrder
 *
 * Correction 2026-07-18: the modal never scrolls (line rows fit the space and
 * page instead), the line grid is tightened (attribute chip next to the item,
 * managed column gaps, reduced row height) and the modal document field is
 * labelled "Purchase Order".
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    VAS.VAS_097_ExpectedGRNWidget = function () {

        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root = $('<div class="vas-egrn-root">');
        var $body, $busy, $footer, $pageInfo, $pageText, $prevBtn, $nextBtn, $countPill;
        var $dialog, $dialogBody, $dialogTitle, $dialogBadge, $dialogBusy;

        var ROWS = [];
        var rowsById = {};
        var currentPO = null;
        var currentLines = [];
        /* Line entry is paged, never scrolled: rcvPageSize adapts to the
           height the modal can give the list (fitModalLines). Typed received
           quantities live in lineEntry (poLineId -> value) so they survive
           page switches. */
        var RCV_PAGE_MAX = 4;
        var rcvPageSize = RCV_PAGE_MAX;
        var rcvPageNo = 1;
        var lineEntry = {};
        var pageNo = 1;
        /* Lines shown on the card before paging. */
        var CARD_ROWS = 4;
        var pageSize = CARD_ROWS;
        var totalPages = 0;
        var totalRecords = 0;
        var loading = false;
        var rowResizeObserver = null;
        var modalResizeObserver = null;
        var modalFitRaf = null;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        /* Review #8 (common): digit grouping follows the currency's ISO 4217 family -
           Indian grouping for INR/PKR/BDT/NPR/BTN/LKR, international otherwise.
           Precision and symbol are read from the system (never hard-coded). */
        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function currencyLocale(isoCode) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(isoCode || '').toUpperCase()) >= 0 ? 'en-IN' : 'en-US';
        }

        function formatMoney(value, precision, symbol, isoCode) {
            var p = Number(precision);
            if (!isFinite(p) || p < 0) { p = 2; }
            var num = Number(value || 0).toLocaleString(currencyLocale(isoCode), {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
            return (symbol ? symbol + ' ' : '') + num;
        }

        function toInputValue(value) {
            var num = Number(value || 0);
            if (!isFinite(num)) { return "0"; }
            return String(Math.round(num * 1000000) / 1000000);
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-egrn-hidden', !show);
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy.toggleClass('vas-egrn-hidden', !show);
        }

        function setBadge(text, cls) {
            if (!$dialogBadge) { return; }
            if (!text) {
                $dialogBadge.addClass('vas-egrn-hidden').empty();
                return;
            }
            $dialogBadge
                .html('<span class="vas-egrn-pill ' + escapeHtml(cls || "info") + '">' + escapeHtml(text) + '</span>')
                .removeClass('vas-egrn-hidden');
        }

        function measurePageSize() {
            /* The card ALWAYS shows CARD_ROWS lines; anything beyond that pages.
               The count is fixed rather than measured so the layout is predictable at every
               resolution - the rows themselves flex to share the list height (see the CSS
               .vas-egrn-rows > .vas-egrn-row rule), so 4 rows fill the card exactly whether it is
               rendered on a 1080p or a 4K screen. Because the count is a constant there is no
               measure-then-resize feedback loop. */
            return CARD_ROWS;
        }

        function syncPageSize() {
            var nextPageSize = measurePageSize();
            if (nextPageSize === pageSize) { return; }

            var firstRecord = ((pageNo - 1) * pageSize) + 1;
            pageSize = nextPageSize;
            pageNo = Math.max(1, Math.ceil(firstRecord / pageSize));
            if (!loading) { loadExpected(pageNo); }
        }

        this.initalize = function () {
            createWidget();
            createDialog();
        };

        function createWidget() {
            var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';

            var $card = $('<div class="vas-egrn-card vas-widget-bg">');

            var $header = $(
                '<div class="vas-egrn-head">' +
                '<span class="vas-egrn-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/></svg>' +
                '</span>' +
                '<div class="vas-egrn-titles">' +
                '<div class="vas-egrn-title">' + escapeHtml(lbl("VAS_ExpectedGRN", "Expected GRN")) + '</div>' +
                '</div>' +
                '<span class="vas-egrn-count vas-egrn-hidden"></span>' +
                '</div>'
            );

            $body = $(
                '<div class="vas-egrn-body">' +
                '<div class="vas-egrn-rows"></div>' +
                '</div>'
            );

            $footer = $(
                '<div class="vas-egrn-foot vas-egrn-hidden">' +
                '<span class="vas-egrn-foot-info"></span>' +
                '<div class="vas-egrn-pager">' +
                '<button type="button" class="vas-egrn-pgbtn vas-egrn-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' + chevL + '</button>' +
                '<span class="vas-egrn-pgtext"></span>' +
                '<button type="button" class="vas-egrn-pgbtn vas-egrn-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' + chevR + '</button>' +
                '</div>' +
                '</div>'
            );

            $countPill = $header.find('.vas-egrn-count');
            $pageInfo = $footer.find('.vas-egrn-foot-info');
            $pageText = $footer.find('.vas-egrn-pgtext');
            $prevBtn = $footer.find('.vas-egrn-prev');
            $nextBtn = $footer.find('.vas-egrn-next');

            $card.append($header).append($body).append($footer);
            $root.append($card);

            $busy = $('<div class="vas-egrn-busy vas-egrn-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $body.on('click', '.vas-egrn-row', function () {
                selectPO(Number($(this).data('poid') || 0));
            });
            $prevBtn.on('click', function () {
                if (!loading && pageNo > 1) { loadExpected(pageNo - 1); }
            });
            $nextBtn.on('click', function () {
                if (!loading && pageNo < totalPages) { loadExpected(pageNo + 1); }
            });

            if (window.ResizeObserver) {
                rowResizeObserver = new ResizeObserver(function () {
                    window.setTimeout(syncPageSize, 0);
                });
                rowResizeObserver.observe($body.find('.vas-egrn-rows')[0]);
            }
        }

        function loadExpected(page) {
            loading = true;
            showBusy(true);
            if ($prevBtn) { $prevBtn.prop("disabled", true); }
            if ($nextBtn) { $nextBtn.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_097_ExpectedGRNWidget/GetExpectedPurchaseOrders',
                type: 'GET',
                cache: false,
                data: { pageNo: page, pageSize: pageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    loading = false;
                    showBusy(false);

                    if (!data || data.error) {
                        renderRows([], 0);
                        return;
                    }

                    pageNo = Number(data.pageNo || page);
                    totalPages = Number(data.totalPages || 0);
                    renderRows(data.rows || [], Number(data.totalRecords || 0));
                },
                error: function () {
                    loading = false;
                    showBusy(false);
                    renderRows([], 0);
                }
            });
        }

        function fileIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>';
        }

        function checkIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>';
        }

        function renderRows(rows, totalCount) {
            ROWS = rows || [];
            totalRecords = Number(totalCount || 0);
            rowsById = {};

            if ($countPill) {
                if (totalRecords > 0) {
                    $countPill.text(lbl("VAS_GRNCount", "GRN Count") + ' ' + totalRecords).removeClass('vas-egrn-hidden');
                } else {
                    $countPill.addClass('vas-egrn-hidden').empty();
                }
            }

            var $rows = $body.find('.vas-egrn-rows');
            $rows.empty();

            if (ROWS.length === 0) {
                $rows.append('<div class="vas-egrn-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div>');
                updatePager();
                return;
            }

            for (var i = 0; i < ROWS.length; i++) {
                var r = ROWS[i];
                rowsById[r.poId] = r;

                var valueText = formatMoney(r.poValue, r.stdPrecision, r.curSymbol, r.currencyIso);

                $rows.append(
                    '<button type="button" class="vas-egrn-row" data-poid="' + escapeHtml(r.poId) + '">' +
                    '<span class="vas-egrn-fi">' + fileIcon() + '</span>' +
                    '<div class="vas-egrn-main">' +
                    '<div class="vas-egrn-top">' +
                    '<span class="vas-egrn-no" title="' + escapeHtml(r.poNo) + '">' + escapeHtml(r.poNo) + '</span>' +
                    '<span class="vas-egrn-qty" title="' + escapeHtml(lbl("VAS_NoOfLines", "No of Lines")) + '">' + escapeHtml(r.lineCount) + '</span>' +
                    '</div>' +
                    '<div class="vas-egrn-mid">' +
                    '<span class="vas-egrn-party" title="' + escapeHtml(r.supplier) + '">' + escapeHtml(r.supplier) + '</span>' +
                    '<span class="vas-egrn-val" title="' + escapeHtml(valueText) + '">' + escapeHtml(valueText) + '</span>' +
                    '</div>' +
                    '<div class="vas-egrn-sub">' +
                    '<span class="vas-egrn-addr" title="' + escapeHtml(r.addressLine) + '">' + escapeHtml(r.addressLine || '-') + '</span>' +
                    '<span class="vas-egrn-wh" title="' + escapeHtml(r.warehouseName) + '">' + escapeHtml(r.warehouseName || '-') + '</span>' +
                    '</div>' +
                    '</div>' +
                    '</button>'
                );
            }

            updatePager();
            window.setTimeout(syncPageSize, 0);
        }

        function updatePager() {
            var shownPages = totalPages > 0 ? totalPages : 1;
            if ($pageText) {
                $pageText.text(pageNo + ' ' + lbl("VAS_Of", "of") + ' ' + shownPages);
            }
            if ($pageInfo) {
                var from = totalRecords > 0 ? ((pageNo - 1) * pageSize + 1) : 0;
                var to = totalRecords > 0 ? Math.min(pageNo * pageSize, totalRecords) : 0;
                $pageInfo.text(lbl("VAS_Showing", "Showing") + ' ' + from + '-' + to + ' ' + lbl("VAS_Of", "of") + ' ' + totalRecords);
            }
            if ($prevBtn) { $prevBtn.prop('disabled', loading || pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', loading || pageNo >= shownPages); }
            // Footer/pager stays visible at all times, even with no data.
            if ($footer) { $footer.removeClass('vas-egrn-hidden'); }
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-egrn-dialog vas-egrn-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-egrn-scrim"></div>' +
                '<div class="vas-egrn-modal">' +
                '<div class="vas-egrn-modal-head">' +
                '<div class="vas-egrn-modal-title-wrap">' +
                '<button type="button" class="vas-egrn-back" aria-label="' + escapeHtml(lbl("VAS_Back", "Back")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<h3 class="vas-egrn-modal-title"></h3>' +
                '<span class="vas-egrn-modal-badge vas-egrn-hidden"></span>' +
                '</div>' +
                '<button type="button" class="vas-egrn-modal-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-egrn-modal-body"></div>' +
                '<div class="vas-egrn-modal-busy vas-egrn-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>' +
                '</div>'
            );

            $dialogBody = $dialog.find('.vas-egrn-modal-body');
            $dialogTitle = $dialog.find('.vas-egrn-modal-title');
            $dialogBadge = $dialog.find('.vas-egrn-modal-badge');
            $dialogBusy = $dialog.find('.vas-egrn-modal-busy');

            $dialog.find('.vas-egrn-modal-close').on('click', closeDialog);
            $dialog.find('.vas-egrn-scrim').on('click', closeDialog);
            $dialog.find('.vas-egrn-back').on('click', closeDialog);

            $dialogBody.on('input', '.vas-egrn-rcv-in', function () {
                var $row = $(this).closest('.vas-egrn-rcv-line[data-lineid]');
                var lineId = Number($row.data('lineid') || 0);
                if (lineId > 0) { lineEntry[lineId] = String($(this).val() || ""); }
                validateLines();
            });
            $dialogBody.on('click', '.vas-egrn-create-btn', createGRN);
            $dialogBody.on('click', '.vas-egrn-rcv-prev', function () { if (rcvPageNo > 1) { rcvPageNo--; renderLineEntry(); } });
            $dialogBody.on('click', '.vas-egrn-rcv-next', function () { rcvPageNo++; renderLineEntry(); });

            /* The modal never scrolls: whenever its size changes (window
               resize, browser zoom), refit how many line rows are shown. */
            if (window.ResizeObserver) {
                modalResizeObserver = new ResizeObserver(function () {
                    if (modalFitRaf) { window.cancelAnimationFrame(modalFitRaf); }
                    modalFitRaf = window.requestAnimationFrame(fitModalLines);
                });
                modalResizeObserver.observe($dialogBody[0]);
            }

            $(document).on('keydown.vas-egrn', function (e) {
                if (e.key === 'Escape' && !$dialog.hasClass('vas-egrn-hidden')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function selectPO(poId) {
            var po = rowsById[poId];
            if (!po || !$dialog) { return; }

            currentPO = po;
            currentLines = [];

            $dialogTitle.text(po.poNo);
            setBadge(lbl("VAS_NoOfLines", "No of Lines") + ' ' + po.lineCount, "info");

            $dialog.removeClass('vas-egrn-hidden');
            $('body').addClass('vas-egrn-body-lock');
            $dialogBody.html('');
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_097_ExpectedGRNWidget/GetPurchaseOrderLines',
                type: 'GET',
                cache: false,
                data: { poId: po.poId },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (data.error) {
                        VIS.ADialog.error("", false, data.error, "");
                        return;
                    }
                    currentLines = data.rows || [];
                    rcvPageNo = 1;
                    rcvPageSize = RCV_PAGE_MAX;
                    lineEntry = {};
                    for (var li = 0; li < currentLines.length; li++) {
                        lineEntry[currentLines[li].poLineId] = toInputValue(currentLines[li].defaultReceivedQty);
                    }
                    renderLineEntry();
                },
                error: function () {
                    showDialogBusy(false);
                    renderLineEntry();
                }
            });
        }

        function fieldHtml(label, value, strong) {
            var shown = value == null || value === "" ? "-" : value;
            return '<div class="vas-egrn-field">' +
                '<div class="vas-egrn-field-lbl">' + escapeHtml(label) + '</div>' +
                '<div class="vas-egrn-field-val' + (strong ? ' strong' : '') + '" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
                '</div>';
        }

        /* Footer pager matching the widget's own pagination design: "Showing
           X-Y of Z" on the left, the ‹ X of Y › pager on the right (same
           vas-egrn-foot / vas-egrn-pager markup and text). */
        function linePagerHtml(pageNo, totalPages, totalRows, pageSize) {
            if (totalPages <= 1) { return ''; }
            var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            var from = (pageNo - 1) * pageSize + 1;
            var to = Math.min(pageNo * pageSize, totalRows);
            return '<div class="vas-egrn-foot">' +
                '<span class="vas-egrn-foot-info">' + escapeHtml(VIS.Msg.getMsg("VAS_Showing") + ' ' + from + '-' + to + ' ' + VIS.Msg.getMsg("VAS_Of") + ' ' + totalRows) + '</span>' +
                '<div class="vas-egrn-pager">' +
                '<button type="button" class="vas-egrn-pgbtn vas-egrn-rcv-prev"' + (pageNo <= 1 ? ' disabled' : '') + '>' + chevL + '</button>' +
                '<span class="vas-egrn-pgtext">' + escapeHtml(pageNo + VIS.Msg.getMsg("VAS_Of") + totalPages) + '</span>' +
                '<button type="button" class="vas-egrn-pgbtn vas-egrn-rcv-next"' + (pageNo >= totalPages ? ' disabled' : '') + '>' + chevR + '</button>' +
                '</div>' +
                '</div>';
        }

        /* Empty attribute-set instances carry dash-only descriptions ("-",
           "---"): those render as a plain dash; real values render as a chip
           sitting right next to the item name, per the reference design. */
        function attrCellHtml(attributeName) {
            var text = (attributeName && !/^-+$/.test(String(attributeName).trim())) ? String(attributeName) : '';
            if (!text) {
                return '<div class="vas-egrn-rcv-attr">-</div>';
            }
            return '<div class="vas-egrn-rcv-attr" title="' + escapeHtml(text) + '"><span class="vas-egrn-attr-chip">' + escapeHtml(text) + '</span></div>';
        }

        function renderLineEntry() {
            if (!currentPO) { return; }

            if (currentLines.length === 0) {
                $dialogBody.html('<div class="vas-egrn-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div>');
                return;
            }

            /* Correction 2026-07-18: the document field is labelled "Purchase
               Order" (core message key), not "Expected GRN". */
            var fields =
                '<div class="vas-egrn-form-grid">' +
                fieldHtml(lbl("PurchaseOrder", "Purchase Order"), currentPO.poNo, true) +
                fieldHtml(lbl("Vendor", "Supplier"), currentPO.supplier) +
                fieldHtml(lbl("VAS_VendorLocation", "Address"), currentPO.addressLine) +
                fieldHtml(lbl("VAS_NoOfLines", "No of Lines"), String(currentLines.length)) +
                '</div>';

            /* Show at most rcvPageSize rows per page; further lines are paged,
               never scrolled (fitModalLines keeps the page size in step with
               the space available). Typed quantities persist in lineEntry. */
            var totalPages = Math.max(1, Math.ceil(currentLines.length / rcvPageSize));
            if (rcvPageNo > totalPages) { rcvPageNo = totalPages; }
            if (rcvPageNo < 1) { rcvPageNo = 1; }
            var start = (rcvPageNo - 1) * rcvPageSize;
            var end = Math.min(start + rcvPageSize, currentLines.length);

            var rows = '';
            for (var i = start; i < end; i++) {
                var line = currentLines[i];
                var qtyValue = lineEntry[line.poLineId] != null ? lineEntry[line.poLineId] : toInputValue(line.defaultReceivedQty);
                rows +=
                    '<div class="vas-egrn-rcv-line" data-lineid="' + escapeHtml(line.poLineId) + '" data-openqty="' + escapeHtml(line.openQty) + '">' +
                    '<div class="vas-egrn-rcv-name" title="' + escapeHtml(line.itemName) + '">' + escapeHtml(line.itemName) + '</div>' +
                    attrCellHtml(line.attributeName) +
                    '<div class="vas-egrn-rcv-po">' + escapeHtml(formatQty(line.poQty)) + '</div>' +
                    '<input class="vas-egrn-rcv-in" type="number" min="0" max="' + escapeHtml(line.openQty) + '" step="any" value="' + escapeHtml(qtyValue) + '" aria-label="' + escapeHtml(lbl("VAS_Received", "Received")) + '"/>' +
                    '<div class="vas-egrn-rcv-uom" title="' + escapeHtml(line.uom) + '">' + escapeHtml(line.uom) + '</div>' +
                    '</div>';
            }

            $dialogBody.html(
                fields +
                '<div class="vas-egrn-note">' + fileIcon() + '<span>' + escapeHtml(lbl("VAS_EnterReceivedQtyAgainstLine", "Enter received quantity against each PO line, then create the GRN.")) + '</span></div>' +
                '<div class="vas-egrn-rcv-line vas-egrn-rcv-head">' +
                '<div>' + escapeHtml(lbl("VAS_Item", "Item")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_Attribute", "Attribute")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_POQty", "PO Qty")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_Received", "Received")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_Uom", "UOM")) + '</div>' +
                '</div>' +
                '<div class="vas-egrn-rcv-viewport"><div class="vas-egrn-lines">' + rows + '</div></div>' +
                linePagerHtml(rcvPageNo, totalPages, currentLines.length, rcvPageSize) +
                '<div class="vas-egrn-error vas-egrn-hidden"></div>' +
                '<div class="vas-egrn-action"><button type="button" class="vas-egrn-create-btn">' + checkIcon() + '<span>' + escapeHtml(lbl("VAS_097_CreateGRN", "Create GRN")) + '</span></button></div>'
            );

            validateLines();
            window.setTimeout(fitModalLines, 0);
        }

        /* The modal never scrolls: the line rows live in a clipped viewport
           and rcvPageSize is recomputed to the largest row count that fits
           the space the modal can give it before hitting its height cap -
           zooming in or shrinking the window pages the rows instead of
           showing a scrollbar (reference design behaviour). */
        function fitModalLines() {
            if (!$dialog || $dialog.hasClass('vas-egrn-hidden') || currentLines.length === 0) { return; }
            var $vp = $dialogBody.find('.vas-egrn-rcv-viewport');
            var $row = $vp.find('.vas-egrn-rcv-line').first();
            if (!$vp.length || !$row.length) { return; }
            var rowHeight = $row.outerHeight(true);
            if (!rowHeight || rowHeight <= 0) { return; }

            /* Space the list may use = its current height plus whatever the
               modal can still grow before reaching its 88vh cap (matches the
               CSS max-height on .vas-egrn-modal). */
            var $modal = $dialog.find('.vas-egrn-modal');
            var slack = Math.max(0, Math.floor(window.innerHeight * 0.88) - Math.ceil($modal.outerHeight()));
            var fit = Math.max(1, Math.min(RCV_PAGE_MAX, Math.floor(($vp[0].clientHeight + slack) / rowHeight)));

            if (fit !== rcvPageSize) {
                var firstIndex = (rcvPageNo - 1) * rcvPageSize;
                rcvPageSize = fit;
                rcvPageNo = Math.floor(firstIndex / rcvPageSize) + 1;
                renderLineEntry();
                return;
            }

            /* Widget-development-rules #15: the viewport must not resize
               with the number of lines on the CURRENT page. renderLineEntry
               rebuilds $dialogBody's innerHTML on every call (wiping any
               inline style on $vp), so this has to be reapplied on every
               settled render, not just when rcvPageSize changes above. */
            $vp.css('min-height', Math.ceil(rowHeight * rcvPageSize) + 'px');
        }

        /* Reads every line from lineEntry state (covers ALL pages, not just the
           visible one), so paging never drops a typed quantity. */
        function collectLines() {
            var lines = [];
            var invalid = false;
            var message = "";

            for (var i = 0; i < currentLines.length; i++) {
                var line = currentLines[i];
                var lineId = Number(line.poLineId);
                var openQty = Number(line.openQty || 0);
                var raw = lineEntry[lineId] != null ? lineEntry[lineId] : toInputValue(line.defaultReceivedQty);
                var qty = Number(raw);

                if (!isFinite(qty) || qty < 0) {
                    invalid = true;
                    message = lbl("VAS_NegativeReceivedQty", "Received quantity cannot be negative.");
                    break;
                }
                if (qty > openQty) {
                    invalid = true;
                    message = lbl("VAS_ReceivedQtyTooHigh", "Received quantity cannot be greater than ordered quantity.");
                    break;
                }
                if (qty > 0) {
                    lines.push({ poLineId: lineId, receivedQty: qty });
                }
            }

            return { lines: lines, invalid: invalid, message: message };
        }

        function validateLines() {
            var result = collectLines();
            var $error = $dialogBody.find('.vas-egrn-error');
            var $button = $dialogBody.find('.vas-egrn-create-btn');

            /* Flag invalid rows visible on the current page. */
            $dialogBody.find('.vas-egrn-rcv-line[data-lineid]').each(function () {
                var $row = $(this);
                var openQty = Number($row.data('openqty') || 0);
                var qty = Number($row.find('.vas-egrn-rcv-in').val() || 0);
                $row.toggleClass('invalid', !isFinite(qty) || qty < 0 || qty > openQty);
            });

            if (result.invalid) {
                $error.text(result.message).removeClass('vas-egrn-hidden');
                $button.prop('disabled', true);
                return false;
            }

            if (result.lines.length === 0) {
                $error.text(lbl("VAS_ReceivedQtyRequired", "Enter received quantity for at least one line.")).removeClass('vas-egrn-hidden');
                $button.prop('disabled', true);
                return false;
            }

            $error.addClass('vas-egrn-hidden').empty();
            $button.prop('disabled', false);
            return true;
        }

        function createGRN() {
            if (!currentPO || !validateLines()) { return; }

            var result = collectLines();
            var $button = $dialogBody.find('.vas-egrn-create-btn');

            $button.prop('disabled', true);
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_097_ExpectedGRNWidget/CreateGRN',
                type: 'POST',
                data: {
                    poId: currentPO.poId,
                    linesJson: JSON.stringify(result.lines)
                },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (data.error || data.success === false) {
                        VIS.ADialog.error("", false, data.message || data.error || lbl("VAS_GRNCouldNotBeCreated", "GRN could not be created."), "");
                        $button.prop('disabled', false);
                        return;
                    }

                    closeDialog();
                    $self.refreshWidget();
                    $(document).trigger('VAS_GRNCreated', [data]);
                },
                error: function () {
                    showDialogBusy(false);
                    VIS.ADialog.error("", false, lbl("VAS_GRNCouldNotBeCreated", "GRN could not be created."), "");
                    $button.prop('disabled', false);
                }
            });
        }

        function closeDialog() {
            if (!$dialog) { return; }
            currentPO = null;
            currentLines = [];
            rcvPageNo = 1;
            rcvPageSize = RCV_PAGE_MAX;
            $dialog.addClass('vas-egrn-hidden');
            $('body').removeClass('vas-egrn-body-lock');
        }

        this.refreshWidget = function () {
            pageNo = 1;
            totalPages = 0;
            loadExpected(1);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-egrn');
            $('body').removeClass('vas-egrn-body-lock');
            if (rowResizeObserver) { rowResizeObserver.disconnect(); rowResizeObserver = null; }
            if (modalResizeObserver) { modalResizeObserver.disconnect(); modalResizeObserver = null; }
            if (modalFitRaf) { window.cancelAnimationFrame(modalFitRaf); modalFitRaf = null; }
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_097_ExpectedGRNWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_097_ExpectedGRNWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_097_ExpectedGRNWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
        var ssef = this;
        window.setTimeout(function () {
            ssef.refreshWidget();
        }, 50);
    };

    VAS.VAS_097_ExpectedGRNWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_097_ExpectedGRNWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_097_ExpectedGRNWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
