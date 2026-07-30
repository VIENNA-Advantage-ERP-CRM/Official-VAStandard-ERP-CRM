/************************************************************
 * Module Name    : VAS
 * Purpose        : Pending GRN dashboard - overdue vendor purchase orders
 *                  (promised before today) that still have quantity to
 *                  receive. A row click opens the shared GRN modal where
 *                  order lines are picked with checkboxes, the received
 *                  quantity is edited per line and the GRN is generated.
 * Backend        : VAS_097_ExpectedGRNWidget/GetExpectedPurchaseOrders (type PG)
 *                  VAS_097_ExpectedGRNWidget/GetPurchaseOrderLines
 *                  VAS_097_ExpectedGRNWidget/CreateGRN
 * chronological  : Development
 * Created Date   : 20 Sep 2024
 * Created by     : VAI050
 * Correction     : 2026-07-18 - purchase orders stay listed until every line
 *                  is fully delivered or the PO is closed (draft GRN qty no
 *                  longer hides them), the modal never scrolls (line rows fit
 *                  the available space and page instead), tightened line
 *                  grid, no navigation to the created GRN, and the modal
 *                  document field is labelled "Purchase Order".
 *
 * Labels / Message Keys
 *  #  | Current Text                                     | Message Key
 * ----+--------------------------------------------------+---------------------------
 *  1  | Pending GRN                                      | VAS_PendingGRN
 *  2  | GRN Count                                        | VAS_GRNCount
 *  3  | Purchase Order                                   | PurchaseOrder
 *  4  | Vendor                                           | Vendor
 *  5  | Vendor Location                                  | VAS_VendorLocation
 *  6  | No of Lines                                      | VAS_NoOfLines
 *  7  | Item                                             | VAS_Item
 *  8  | Attribute                                        | VAS_Attribute
 *  9  | Remaining Qty                                    | VAS_RemianingQty
 * 10  | UOM                                              | VAS_Uom
 * 11  | Select the order lines to receive, then create.. | VAS_SelectLinesThenGRN
 * 12  | Generate GRN                                     | VAS_GenerateGRN
 * 13  | Showing / of                                     | VAS_Showing / VAS_Of
 * 14  | No data available                                | VAS_NoDataAvailable
 * 15  | Back / Close                                     | VAS_Back / VAS_Close
 * 16  | Document No / Total Amount / Product Location    | Document_No / TotalAmount / VAS_ProductLocation
 * 17  | Received quantity must be between 0 and the ...  | VAS_ReceivedQtyInvalid
 * 18  | GRN could not be generated.                      | VAS_DeliveryOrderNotGenerated
 ***********************************************************/
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

    VAS.VAS_093_PendingGRNWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        // Review #25: the root carries the Expected GRN design scope so this
        // widget renders with the same glass card / rows / pager styling.
        var $root = $('<div class="h-100 w-100 vas-egrn-root">'); // Root container
        this.currentPage = 1;
        this.totalPages = 0;
        var widgetID = 0;
        var pageSize = 4;
        var isLoading = false;
        var rowResizeObserver = null;
        var rowsByPoId = {}; // poId -> list row (feeds the drill-down modal)
        var selectedOrderLineIDs = []; // Array to keep track of selected order line IDs
        /* The modal line list is paged, never scrolled: linePageSize adapts to
           the height the modal can give the list (fitModalLines). Checkbox
           picks live in selectedOrderLineIDs and typed quantities in
           lineQtyById (poLineId -> value) so both survive page switches. */
        var LINE_PAGE_MAX = 4;
        var linePageSize = LINE_PAGE_MAX;
        var linePageNo = 1;
        var lineQtyById = {};
        // Review #25 (follow-up): the drill-down opens the same modal shell the
        // Expected GRN widget uses instead of the old inline panel.
        var $dialog, $dialogBody, $dialogTitle, $dialogBadge, $dialogBusy;
        var modalResizeObserver = null;
        var modalFitRaf = null;
        var currentOrder = null;
        var currentChildRecords = [];

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

        // Review #8 (common): currencies of Indian-numbering countries get Indian
        // digit grouping; all others get international grouping. The backend sends
        // the currency symbol (which is the ISO code when no symbol is configured)
        // and the standard precision - nothing is hardcoded here.
        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];
        var INDIAN_CURRENCY_SYMBOLS = ['₹', 'Rs', 'Rs.', '₨', '৳', 'Nu.', 'रू'];

        function usesIndianNumbering(symbolOrIso) {
            var value = String(symbolOrIso || '').trim();
            return INDIAN_NUMBERING_CURRENCIES.indexOf(value.toUpperCase()) >= 0
                || INDIAN_CURRENCY_SYMBOLS.indexOf(value) >= 0;
        }

        function formatMoney(value, symbolOrIso, precision) {
            var p = Number(precision);
            if (!isFinite(p) || p < 0) { p = 2; }
            return Number(value || 0).toLocaleString(usesIndianNumbering(symbolOrIso) ? 'en-IN' : 'en-US', {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
        }

        function toInputValue(value) {
            var num = Number(value || 0);
            if (!isFinite(num)) { return "0"; }
            return String(Math.round(num * 1000000) / 1000000);
        }
        this.initalize = function () {
            widgetID = this.widgetInfo.AD_UserHomeWidgetID;
            // Review #25: same structure and classes as the Expected GRN widget
            // (glass card, icon+title head, count pill, flat rows, footer pager).
            const orderContainer =
                '<div id="VAS_DeliveryContainer_' + widgetID + '" class="vas-egrn-card vas-widget-bg">' +
                '    <div class="vas-egrn-head">' +
                '        <span class="vas-egrn-ico">' +
                '            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/></svg>' +
                '        </span>' +
                '        <div class="vas-egrn-titles">' +
                '            <div class="vas-egrn-title">' + VIS.Msg.getMsg("VAS_PendingGRN") + '</div>' +
                '        </div>' +
                '        <span class="vas-egrn-count">' + VIS.Msg.getMsg("VAS_GRNCount") + ' <span id="VAS_DeliveryCount_' + widgetID + '">0</span></span>' +
                '    </div>' +
                '    <div class="vas-egrn-body">' +
                '        <div id="VAS_DeliveryBox_' + widgetID + '" class="vas-egrn-rows"></div>' +
                '    </div>' +
                '    <div class="vas-egrn-foot">' +
                '        <span class="vas-egrn-foot-info" id="VAS_FootInfo_' + widgetID + '"></span>' +
                '        <div class="VAS-pagination-container"></div>' +
                '    </div>' +
                '</div>';
            // Create busy indicator
            createBusyIndicator();

            $root.append(orderContainer);
            createDialog();
            bindResizeObserver();

            // Attach click event listener to delivery rows (delegated once).
            $root.on('click', '.vas-pgrn-row', function () {
                openOrderDialog(rowsByPoId[Number($(this).data('poid') || 0)]);
            });
        };


        /* This function will load data in widget.
           Correction 2026-07-18: the list is served by the widget-owned
           VAS_097_ExpectedGRNWidget controller (type 'PG' = promised before
           today) whose open quantity is QtyOrdered - QtyDelivered, so a
           purchase order stays listed until every line is fully received or
           the PO is closed - a GRN saved with less quantity than the PO no
           longer removes it. */
        this.intialLoad = function (pageNo) {
            // Show busy indicator
            isLoading = true;
            $bsyDiv.css('visibility', 'visible');
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_097_ExpectedGRNWidget/GetExpectedPurchaseOrders",
                data: { pageNo: pageNo, pageSize: pageSize, type: "PG" },
                dataType: 'json',
                cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    var rows = (data && !data.error && data.rows) ? data.rows : [];
                    var $box = $root.find('#VAS_DeliveryBox_' + widgetID);
                    $box.empty();
                    rowsByPoId = {};
                    if (rows.length > 0) {
                        // Review #25: rows use the Expected GRN row layout -
                        // doc no + line-count pill / supplier + value / locations.
                        for (var i = 0; i < rows.length; i++) {
                            var order = rows[i];
                            rowsByPoId[order.poId] = order;
                            var amountText = (order.curSymbol ? order.curSymbol + ' ' : '')
                                + formatMoney(order.poValue, order.currencyIso || order.curSymbol, order.stdPrecision);
                            var boxHtml = (
                                '<button type="button" class="vas-egrn-row vas-pgrn-row" data-poid="' + escapeHtml(order.poId) + '">' +
                                '<span class="vas-egrn-fi">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>' +
                                '</span>' +
                                '<div class="vas-egrn-main">' +
                                '<div class="vas-egrn-top">' +
                                '<span class="vas-egrn-no" title="' + escapeHtml(VIS.Msg.getMsg("Document_No")) + '">' + escapeHtml(order.poNo) + '</span>' +
                                '<span class="vas-egrn-qty" title="' + escapeHtml(VIS.Msg.getMsg("VAS_NoOfLines")) + '">' + escapeHtml(order.lineCount) + '</span>' +
                                '</div>' +
                                '<div class="vas-egrn-mid">' +
                                '<span class="vas-egrn-party" title="' + escapeHtml(VIS.Msg.getMsg("Vendor")) + '">' + escapeHtml(order.supplier) + '</span>' +
                                '<span class="vas-egrn-val" title="' + escapeHtml(VIS.Msg.getMsg("TotalAmount")) + '">' + escapeHtml(amountText) + '</span>' +
                                '</div>' +
                                '<div class="vas-egrn-sub">' +
                                '<span class="vas-egrn-addr" title="' + escapeHtml(VIS.Msg.getMsg("VAS_VendorLocation")) + '">' + escapeHtml(order.addressLine || '-') + '</span>' +
                                '<span class="vas-egrn-wh" title="' + escapeHtml(VIS.Msg.getMsg("VAS_ProductLocation")) + '">' + escapeHtml(order.warehouseName || '-') + '</span>' +
                                '</div>' +
                                '</div>' +
                                '</button>');
                            $box.append(boxHtml);
                        }
                        $self.recordCount = Number(data.totalRecords || rows.length);
                        $root.find('#VAS_DeliveryCount_' + widgetID).text($self.recordCount);
                        buildPagination($self.recordCount);
                        $root.find('#VAS_PaginationText_' + widgetID).text($self.currentPage + VIS.Msg.getMsg("VAS_Of") + $self.totalPages);
                        // Review #25: "Showing x-y of N" footer info like Expected GRN.
                        var fromRecord = ($self.currentPage - 1) * pageSize + 1;
                        var toRecord = fromRecord + rows.length - 1;
                        $root.find('#VAS_FootInfo_' + widgetID).text(
                            VIS.Msg.getMsg("VAS_Showing") + ' ' + fromRecord + '-' + toRecord + ' ' + VIS.Msg.getMsg("VAS_Of") + ' ' + ($self.recordCount || toRecord)
                        );
                    }
                    else {
                        $box.html(
                            '<div class="vas-egrn-empty">' + VIS.Msg.getMsg("VAS_NoDataAvailable") + '</div>'
                        );
                        $root.find('#VAS_DeliveryCount_' + widgetID).text('0');
                        $root.find('#VAS_FootInfo_' + widgetID).text('');
                        $root.find('.VAS-pagination-container').empty();
                    }
                    window.setTimeout(syncPageSize, 0);
                    isLoading = false;
                    $bsyDiv.css('visibility', 'hidden');

                },
                error: function (xhr, status, error) {
                    // Handle errors
                    console.log('Failed to fetch data:', status, error);
                    isLoading = false;
                    $bsyDiv[0].style.visibility = "hidden";
                }
            });
        };

        function measurePageSize() {
            var $list = $root.find('#VAS_DeliveryBox_' + widgetID);
            if (!$list.length || !$list.is(':visible')) { return pageSize; }

            var listHeight = Math.floor($list.innerHeight());
            if (listHeight <= 0) { return pageSize; }

            var $sample = $list.find('.vas-egrn-row:first');
            var rowHeight = $sample.length ? Math.ceil($sample.outerHeight(true)) : 58;
            if (rowHeight <= 0) { rowHeight = 58; }

            /* Paged like the Material Receipt Register: at least 4 records on
               the standard card; the count still grows with taller screens. */
            return Math.max(4, Math.floor(listHeight / rowHeight));
        }

        function syncPageSize() {
            var nextPageSize = measurePageSize();
            if (nextPageSize === pageSize) { return; }

            var firstRecord = (($self.currentPage - 1) * pageSize) + 1;
            pageSize = nextPageSize;
            $self.currentPage = Math.max(1, Math.ceil(firstRecord / pageSize));

            if (!isLoading) {
                $self.intialLoad($self.currentPage);
            }
        }

        function bindResizeObserver() {
            var $list = $root.find('#VAS_DeliveryBox_' + widgetID);
            if (!$list.length || typeof ResizeObserver === 'undefined') { return; }
            if (rowResizeObserver) { rowResizeObserver.disconnect(); }

            rowResizeObserver = new ResizeObserver(syncPageSize);
            rowResizeObserver.observe($list[0]);
        }

        /* Review #25 (follow-up): the drill-down uses the same modal shell,
           form grid, line table and primary action as the Expected GRN widget.
           Order lines are selected with checkboxes and the GRN is generated
           for the selection. */
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

            $dialogBody.on('change', '.vas-pgrn-check', function () {
                var orderlineID = Number($(this).data('orderlineid'));
                if ($(this).is(':checked')) {
                    if (selectedOrderLineIDs.indexOf(orderlineID) < 0) { selectedOrderLineIDs.push(orderlineID); }
                } else {
                    selectedOrderLineIDs = selectedOrderLineIDs.filter(function (id) { return id !== orderlineID; });
                }
                updateGenerateState();
            });
            $dialogBody.on('input', '.vas-pgrn-qty', function () {
                var orderLineId = Number($(this).data('orderlineid') || 0);
                if (orderLineId > 0) { lineQtyById[orderLineId] = String($(this).val() || ""); }
                updateGenerateState();
            });
            $dialogBody.on('click', '.vas-egrn-create-btn', function () {
                if (currentOrder) { generateGRN(currentOrder.poId); }
            });
            $dialogBody.on('click', '.vas-egrn-rcv-prev', function () { if (linePageNo > 1) { linePageNo--; renderOrderLines(); } });
            $dialogBody.on('click', '.vas-egrn-rcv-next', function () { linePageNo++; renderOrderLines(); });

            /* The modal never scrolls: whenever its size changes (window
               resize, browser zoom), refit how many line rows are shown. */
            if (typeof ResizeObserver !== 'undefined') {
                modalResizeObserver = new ResizeObserver(function () {
                    if (modalFitRaf) { window.cancelAnimationFrame(modalFitRaf); }
                    modalFitRaf = window.requestAnimationFrame(fitModalLines);
                });
                modalResizeObserver.observe($dialogBody[0]);
            }

            $(document).on('keydown.vas-pgrn-' + widgetID, function (e) {
                if (e.key === 'Escape' && $dialog && !$dialog.hasClass('vas-egrn-hidden')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.addClass('vas-egrn-hidden');
            $('body').removeClass('vas-egrn-body-lock');
            currentOrder = null;
            selectedOrderLineIDs = [];
            currentChildRecords = [];
            lineQtyById = {};
            linePageNo = 1;
            linePageSize = LINE_PAGE_MAX;
            showDialogBusy(false);
        }

        function showDialogBusy(show) {
            if ($dialogBusy) { $dialogBusy.toggleClass('vas-egrn-hidden', !show); }
        }

        function setDialogError(message) {
            var $error = $dialogBody.find('.vas-egrn-error');
            $error.text(message || '').toggleClass('vas-egrn-hidden', !message);
        }

        /* The child line for an order-line id in the current order (for its
           remaining-qty cap), from the persisted list - not the DOM, which only
           holds the visible page. */
        function childLineById(orderLineId) {
            for (var i = 0; i < currentChildRecords.length; i++) {
                if (Number(currentChildRecords[i].poLineId) === Number(orderLineId)) { return currentChildRecords[i]; }
            }
            return null;
        }

        /* Reads a selected line's editable quantity from lineQtyById state (so it
           works across pages); null when invalid (negative, not a number, or
           above the remaining quantity). */
        function selectedLineQty(orderLineId) {
            var raw = lineQtyById[orderLineId];
            if (raw == null) { return null; }
            var qty = Number(String(raw).replace(/,/g, ""));
            var line = childLineById(orderLineId);
            var max = line ? Number(line.openQty) : 0;
            if (!isFinite(qty) || qty <= 0) { return null; }
            if (isFinite(max) && max > 0 && qty > max + 0.000001) { return null; }
            return qty;
        }

        function updateGenerateState() {
            var valid = selectedOrderLineIDs.length > 0;
            for (var i = 0; i < selectedOrderLineIDs.length && valid; i++) {
                if (selectedLineQty(selectedOrderLineIDs[i]) == null) { valid = false; }
            }
            $dialogBody.find('.vas-egrn-create-btn').prop('disabled', !valid);
        }

        function fieldHtml(label, value, strong) {
            var shown = value == null || value === "" ? "-" : value;
            return '<div class="vas-egrn-field">' +
                '<div class="vas-egrn-field-lbl">' + escapeHtml(label) + '</div>' +
                '<div class="vas-egrn-field-val' + (strong ? ' strong' : '') + '" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
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

        /* Footer pager matching the widget's own pagination design: "Showing
           X-Y of Z" on the left, the ‹ X of Y › pager on the right (same
           vas-egrn-foot / vas-egrn-pager markup and text). */
        function linePagerHtml(pageNo, totalPages, totalRows, pageSizeUsed) {
            if (totalPages <= 1) { return ''; }
            var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            var from = (pageNo - 1) * pageSizeUsed + 1;
            var to = Math.min(pageNo * pageSizeUsed, totalRows);
            return '<div class="vas-egrn-foot">' +
                '<span class="vas-egrn-foot-info">' + escapeHtml(VIS.Msg.getMsg("VAS_Showing") + ' ' + from + '-' + to + ' ' + VIS.Msg.getMsg("VAS_Of") + ' ' + totalRows) + '</span>' +
                '<div class="vas-egrn-pager">' +
                '<button type="button" class="vas-egrn-pgbtn vas-egrn-rcv-prev"' + (pageNo <= 1 ? ' disabled' : '') + '>' + chevL + '</button>' +
                '<span class="vas-egrn-pgtext">' + escapeHtml(pageNo + VIS.Msg.getMsg("VAS_Of") + totalPages) + '</span>' +
                '<button type="button" class="vas-egrn-pgbtn vas-egrn-rcv-next"' + (pageNo >= totalPages ? ' disabled' : '') + '>' + chevR + '</button>' +
                '</div>' +
                '</div>';
        }

        function openOrderDialog(order) {
            if (!$dialog || !order) { return; }

            currentOrder = order;
            selectedOrderLineIDs = [];
            linePageNo = 1;
            linePageSize = LINE_PAGE_MAX;
            lineQtyById = {};
            currentChildRecords = [];

            $dialogTitle.text(order.poNo || '');
            $dialogBadge
                .removeClass('vas-egrn-hidden')
                .html('<span class="vas-egrn-pill info">' + escapeHtml(lbl("VAS_NoOfLines", "No of Lines") + ' ' + order.lineCount) + '</span>');

            $dialog.removeClass('vas-egrn-hidden');
            $('body').addClass('vas-egrn-body-lock');
            $dialogBody.html('');
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_097_ExpectedGRNWidget/GetPurchaseOrderLines",
                type: 'GET',
                cache: false,
                data: { poId: order.poId },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    currentChildRecords = (data && !data.error && data.rows) ? data.rows : [];
                    for (var i = 0; i < currentChildRecords.length; i++) {
                        lineQtyById[currentChildRecords[i].poLineId] = toInputValue(currentChildRecords[i].openQty);
                    }
                    $dialogBadge.html('<span class="vas-egrn-pill info">' + escapeHtml(lbl("VAS_NoOfLines", "No of Lines") + ' ' + currentChildRecords.length) + '</span>');
                    renderOrderLines();
                },
                error: function () {
                    showDialogBusy(false);
                    renderOrderLines();
                }
            });
        }

        /* Paginated line render. Checkbox picks and typed quantities are
           restored from state so switching pages never loses them; the action
           button stays pinned below the pager. */
        function renderOrderLines() {
            if (!currentOrder) { return; }

            var childRecords = currentChildRecords;
            var order = currentOrder;

            if (childRecords.length === 0) {
                $dialogBody.html('<div class="vas-egrn-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div>');
                return;
            }

            /* Correction 2026-07-18: the document field is labelled "Purchase
               Order" (core message key), not "Pending GRN". */
            var fields =
                '<div class="vas-egrn-form-grid">' +
                fieldHtml(lbl("PurchaseOrder", "Purchase Order"), order.poNo, true) +
                fieldHtml(lbl("Vendor", "Vendor"), order.supplier) +
                fieldHtml(lbl("VAS_VendorLocation", "Vendor Location"), order.addressLine) +
                fieldHtml(lbl("VAS_NoOfLines", "No of Lines"), String(childRecords.length)) +
                '</div>';

            var totalPages = Math.max(1, Math.ceil(childRecords.length / linePageSize));
            if (linePageNo > totalPages) { linePageNo = totalPages; }
            if (linePageNo < 1) { linePageNo = 1; }
            var start = (linePageNo - 1) * linePageSize;
            var end = Math.min(start + linePageSize, childRecords.length);

            var rows = '';
            for (var i = start; i < end; i++) {
                var line = childRecords[i];
                var checked = selectedOrderLineIDs.indexOf(Number(line.poLineId)) >= 0 ? ' checked' : '';
                var qtyValue = lineQtyById[line.poLineId] != null ? lineQtyById[line.poLineId] : toInputValue(line.openQty);
                rows +=
                    '<div class="vas-egrn-rcv-line vas-pgrn-line">' +
                    '<label class="vas-egrn-rcv-name vas-pgrn-name" title="' + escapeHtml(line.itemName) + '">' +
                    '<input type="checkbox" class="vas-pgrn-check" data-orderlineid="' + escapeHtml(line.poLineId) + '"' + checked + '/>' +
                    '<span>' + escapeHtml(line.itemName) + '</span>' +
                    '</label>' +
                    attrCellHtml(line.attributeName) +
                    /* Editable received quantity, defaulting to the remaining qty. */
                    '<input class="vas-egrn-rcv-in vas-pgrn-qty" type="number" min="0" max="' + escapeHtml(line.openQty) + '" step="any" value="' + escapeHtml(qtyValue) + '" data-orderlineid="' + escapeHtml(line.poLineId) + '" aria-label="' + escapeHtml(lbl("VAS_RemianingQty", "Remaining Qty")) + '"/>' +
                    '<div class="vas-egrn-rcv-uom" title="' + escapeHtml(line.uom) + '">' + escapeHtml(line.uom) + '</div>' +
                    '</div>';
            }

            $dialogBody.html(
                fields +
                '<div class="vas-egrn-note">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>' +
                '<span>' + escapeHtml(lbl("VAS_SelectLinesThenGRN", "Select the order lines to receive, then create the GRN.")) + '</span>' +
                '</div>' +
                '<div class="vas-egrn-rcv-line vas-pgrn-line vas-egrn-rcv-head">' +
                '<div>' + escapeHtml(lbl("VAS_Item", "Item")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_Attribute", "Attribute")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_RemianingQty", "Remaining Qty")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_Uom", "UOM")) + '</div>' +
                '</div>' +
                '<div class="vas-egrn-rcv-viewport"><div class="vas-egrn-lines">' + rows + '</div></div>' +
                linePagerHtml(linePageNo, totalPages, childRecords.length, linePageSize) +
                '<div class="vas-egrn-error vas-egrn-hidden"></div>' +
                '<div class="vas-egrn-action"><button type="button" class="vas-egrn-create-btn" disabled>' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>' +
                '<span>' + escapeHtml(lbl("VAS_GenerateGRN", "Generate GRN")) + '</span>' +
                '</button></div>'
            );

            updateGenerateState();
            window.setTimeout(fitModalLines, 0);
        }

        /* The modal never scrolls: the line rows live in a clipped viewport
           and linePageSize is recomputed to the largest row count that fits
           the space the modal can give it before hitting its height cap -
           zooming in or shrinking the window pages the rows instead of
           showing a scrollbar (reference design behaviour). */
        function fitModalLines() {
            if (!$dialog || $dialog.hasClass('vas-egrn-hidden') || currentChildRecords.length === 0) { return; }
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
            var fit = Math.max(1, Math.min(LINE_PAGE_MAX, Math.floor(($vp[0].clientHeight + slack) / rowHeight)));

            if (fit !== linePageSize) {
                var firstIndex = (linePageNo - 1) * linePageSize;
                linePageSize = fit;
                linePageNo = Math.floor(firstIndex / linePageSize) + 1;
                renderOrderLines();
                return;
            }

            /* Widget-development-rules #15: the viewport must not resize
               with the number of lines on the CURRENT page. renderOrderLines
               rebuilds $dialogBody's innerHTML on every call (wiping any
               inline style on $vp), so this has to be reapplied on every
               settled render, not just when linePageSize changes above. */
            $vp.css('min-height', Math.ceil(rowHeight * linePageSize) + 'px');
        }

        function generateGRN(poId) {
            if (selectedOrderLineIDs.length === 0) { return; }

            /* Each selected line is sent with its entered quantity to the
               widget-owned VAS_097_ExpectedGRNWidget/CreateGRN endpoint, which
               validates against the live open quantity
               (QtyOrdered - QtyDelivered), then creates AND completes the
               receipt with the plain "MM Receipt" doc type. */
            var lines = [];
            for (var i = 0; i < selectedOrderLineIDs.length; i++) {
                var qty = selectedLineQty(selectedOrderLineIDs[i]);
                if (qty == null) {
                    setDialogError(lbl("VAS_ReceivedQtyInvalid", "Received quantity must be between 0 and the remaining quantity."));
                    return;
                }
                lines.push({ poLineId: selectedOrderLineIDs[i], receivedQty: qty });
            }

            showDialogBusy(true);
            setDialogError('');
            $dialogBody.find('.vas-egrn-create-btn').prop('disabled', true);

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_097_ExpectedGRNWidget/CreateGRN",
                type: 'POST',
                cache: false,
                data: { poId: poId, linesJson: JSON.stringify(lines) },
                success: function (response) {
                    var data = parseResponse(response);
                    /* Normalize: legacy shape used Shipment_ID; the endpoint
                       returns shipmentId/grnId (+ error text on failure). */
                    data.Shipment_ID = Number(data.shipmentId || data.grnId || data.Shipment_ID || 0);
                    if (data.error && !data.message) { data.message = data.error; }
                    showDialogBusy(false);
                    if (data.Shipment_ID > 0) {
                        /* Correction 2026-07-18: stay on the dashboard after
                           the GRN completes - no navigation to the created
                           document; the list simply refreshes. */
                        closeDialog();
                        $(document).trigger('VAS_GRNCreated', [data]);
                        $self.currentPage = 1;
                        $self.intialLoad($self.currentPage);
                    }
                    else {
                        setDialogError(data.message != null && data.message !== ""
                            ? data.message
                            : lbl("VAS_DeliveryOrderNotGenerated", "GRN could not be generated."));
                        updateGenerateState();
                    }
                },
                error: function (xhr, status, error) {
                    console.log('Failed to fetch data:', status, error);
                    showDialogBusy(false);
                    setDialogError(lbl("VAS_DeliveryOrderNotGenerated", "GRN could not be generated."));
                    updateGenerateState();
                }
            });
        }



        /* This function is used to create the busy indicator */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($bsyDiv);
        }



        /* This function builds pagination controls */
        function buildPagination(recordCount) {
            var $paginationContainer = $root.find('.VAS-pagination-container');
            $paginationContainer.empty(); // Clear existing pagination
            $self.totalPages = Math.ceil(recordCount / pageSize); // Update totalPages
            // Review #25: pager styled like the Expected GRN footer pager.
            var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            var $pagination = $('<div class="vas-egrn-pager">' +
                '        <button type="button" id="VAS_Prev_Page_' + widgetID + '" class="vas-egrn-pgbtn">' + chevL + '</button>' +
                '        <span id="VAS_PaginationText_' + widgetID + '" class="vas-egrn-pgtext">' + $self.currentPage + VIS.Msg.getMsg("VAS_Of") + $self.totalPages + '</span>' +
                '        <button type="button" id="VAS_Next_Page_' + widgetID + '" class="vas-egrn-pgbtn">' + chevR + '</button>' +
                '    </div>');

            // Add event listeners for arrows
            $pagination.find('#VAS_Prev_Page_' + widgetID + '').on('click', function (e) {
                e.preventDefault();
                if ($self.currentPage > 1) {
                    $self.currentPage--;
                    $self.intialLoad($self.currentPage);

                }
            });

            $pagination.find('#VAS_Next_Page_' + widgetID + '').on('click', function (e) {
                e.preventDefault();
                if ($self.currentPage < $self.totalPages) {
                    $self.currentPage++;
                    $self.intialLoad($self.currentPage);


                }
            });

            // Disable the arrows at the ends so they read as inactive when there
            // is no previous / next page (same as the Expected GRN and Material
            // Receipt Register widgets); the click guards above are kept too.
            $pagination.find('#VAS_Prev_Page_' + widgetID + '').prop('disabled', $self.currentPage <= 1);
            $pagination.find('#VAS_Next_Page_' + widgetID + '').prop('disabled', $self.currentPage >= $self.totalPages);

            // Append the pagination controls to the container
            $paginationContainer.append($pagination);

        }


        this.getRoot = function () {
            return $root;
        };

        /* This function is used to refresh the widget data */
        this.refreshWidget = function () {
            $self.currentPage = 1;
            $self.totalPages = 0;
            $self.intialLoad($self.currentPage);

        };

        this.disposeComponent = function () {
            if (rowResizeObserver) {
                rowResizeObserver.disconnect();
                rowResizeObserver = null;
            }
            if (modalResizeObserver) {
                modalResizeObserver.disconnect();
                modalResizeObserver = null;
            }
            if (modalFitRaf) {
                window.cancelAnimationFrame(modalFitRaf);
                modalFitRaf = null;
            }
            $(document).off('keydown.vas-pgrn-' + widgetID);
            $('body').removeClass('vas-egrn-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.off();
            $root.remove();
        };
    };

    VAS.VAS_093_PendingGRNWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    VAS.VAS_093_PendingGRNWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_093_PendingGRNWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
        var ssef = this;
        window.setTimeout(function () {
            ssef.intialLoad(1);
        }, 50);
    };

    VAS.VAS_093_PendingGRNWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_093_PendingGRNWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_093_PendingGRNWidget.prototype.dispose = function () {
        this.disposeComponent();
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
