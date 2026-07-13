/************************************************************
 * Module Name    : VAS
 * Purpose        :Get the details of PO and Create GRN
 * chronological  : Development
 * Created Date   : 20 Sep 2024
 * Created by     : VAI050
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

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
        // Create a map to store child records by document number
        var childRecordsMap = {};
        var pageSize = 5;
        var isLoading = false;
        var rowResizeObserver = null;
        var selectedOrderLineIDs = []; // Array to keep track of selected order line IDs
        var AD_Window_ID = 0;
        // Review #25 (follow-up): the drill-down opens the same modal shell the
        // Expected GRN widget uses instead of the old inline panel.
        var $dialog, $dialogBody, $dialogTitle, $dialogBadge, $dialogBusy;
        var currentOrder = null;

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
            //    buildPagination();
        };


        /* This function will load data in widget */
        this.intialLoad = function (pageNo) {
            // Show busy indicator
            isLoading = true;
            $bsyDiv.css('visibility', 'visible');
            $root.find('#VAS_ProductContainer_' + widgetID).remove();
            $root.find('#VAS_DeliveryContainer_' + widgetID).show();
            $.ajax({
                url: VIS.Application.contextUrl + "Product/GetExpectedDelivery",
                data: { pageNo: pageNo, pageSize: pageSize, Type: "PG" },
                dataType: 'json',
                success: function (response) {
                    var response = JSON.parse(response);
                    $root.find('#VAS_DeliveryBox_' + widgetID).empty();
                    $root.find('#VAS_OrderContainer').remove;
                    if (response != null && response.Orders != null && response.Orders.length > 0) {
                        // Review #25: rows use the Expected GRN row layout -
                        // doc no + line-count pill / supplier + value / locations.
                        for (i = 0; i < response.Orders.length; i++) {
                            var order = response.Orders[i];
                            var amountText = order["Symbol"] + ' ' + formatMoney(order["GrandTotal"], order["Symbol"], order["StdPrecision"]);
                            var boxHtml = (
                                '<button type="button" class="vas-egrn-row vas-pgrn-row"' +
                                ' data-doc-no="' + escapeHtml(order["DocumentNo"]) + '"' +
                                ' data-customer-name="' + escapeHtml(order["CustomerName"]) + '"' +
                                ' data-address="' + escapeHtml(order["DeliveryLocation"]) + '"' +
                                ' data-orderid="' + escapeHtml(order["C_Order_ID"]) + '">' +
                                '<span class="vas-egrn-fi">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>' +
                                '</span>' +
                                '<div class="vas-egrn-main">' +
                                '<div class="vas-egrn-top">' +
                                '<span class="vas-egrn-no" title="' + VIS.Msg.getMsg("Document_No") + '">' + order["DocumentNo"] + '</span>' +
                                '<span class="vas-egrn-qty" title="' + VIS.Msg.getMsg("VAS_NoOfLines") + '">' + order["LineCount"] + '</span>' +
                                '</div>' +
                                '<div class="vas-egrn-mid">' +
                                '<span class="vas-egrn-party" title="' + VIS.Msg.getMsg("Vendor") + '">' + order["CustomerName"] + '</span>' +
                                '<span class="vas-egrn-val" title="' + VIS.Msg.getMsg("TotalAmount") + '">' + amountText + '</span>' +
                                '</div>' +
                                '<div class="vas-egrn-sub">' +
                                '<span class="vas-egrn-addr" title="' + VIS.Msg.getMsg("VAS_VendorLocation") + '">' + order["DeliveryLocation"] + '</span>' +
                                '<span class="vas-egrn-wh" title="' + VIS.Msg.getMsg("VAS_ProductLocation") + '">' + order["ProductLocation"] + '</span>' +
                                '</div>' +
                                '</div>' +
                                '</button>');
                            $root.find('#VAS_DeliveryBox_' + widgetID).append(boxHtml);
                        }
                        childRecordsMap = [];
                        // Populate the childRecordsMap with child records
                        response.Orders.forEach(function (order) {
                            if (order.OrderLines && order.OrderLines.length > 0) {
                                childRecordsMap[order.DocumentNo] = order.OrderLines;
                            }
                        });
                        /* Add Pagination div on first tym data load*/
                        if (response.RecordCount != null) {
                            $root.find('#VAS_DeliveryCount_' + widgetID).text(response.RecordCount);
                            $self.recordCount = response.RecordCount;
                            buildPagination(response.RecordCount);
                            AD_Window_ID = response.AD_Window_ID;
                        }
                        $root.find('#VAS_PaginationText_' + widgetID).text($self.currentPage + VIS.Msg.getMsg("VAS_Of") + $self.totalPages);
                        // Review #25: "Showing x-y of N" footer info like Expected GRN.
                        var fromRecord = ($self.currentPage - 1) * pageSize + 1;
                        var toRecord = fromRecord + response.Orders.length - 1;
                        $root.find('#VAS_FootInfo_' + widgetID).text(
                            VIS.Msg.getMsg("VAS_Showing") + ' ' + fromRecord + '-' + toRecord + ' ' + VIS.Msg.getMsg("VAS_Of") + ' ' + ($self.recordCount || toRecord)
                        );
                        // Attach click event listener to delivery rows
                        $root.off('click', '.vas-pgrn-row');
                        $root.on('click', '.vas-pgrn-row', function () {
                            openOrderDialog({
                                docNo: $(this).data('doc-no'),
                                customerName: $(this).data('customer-name'),
                                address: $(this).data('address'),
                                orderId: $(this).data('orderid')
                            });
                        });
                    }
                    else {
                        $root.find('#VAS_DeliveryBox_' + widgetID).html(
                            '<div class="vas-egrn-empty">' + VIS.Msg.getMsg("VAS_NoDataAvailable") + '</div>'
                        );
                        $root.find('#VAS_FootInfo_' + widgetID).text('');
                    }
                    //else {

                    //    // Display "No data available" message
                    //    const message = $('<div class="VAS-data-message">' + VIS.Msg.getMsg("VAS_NoDataAvailable") + '</div>');
                    //    $root.find('.VAS-height-container').append(message);
                    //}
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

            return Math.max(2, Math.floor(listHeight / rowHeight));
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
           Behaviour is unchanged - order lines are selected with checkboxes and
           the GRN is generated for the selection. */
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
            $dialogBody.on('click', '.vas-egrn-create-btn', function () {
                if (currentOrder) { generateGRN(currentOrder.orderId); }
            });

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
            showDialogBusy(false);
        }

        function showDialogBusy(show) {
            if ($dialogBusy) { $dialogBusy.toggleClass('vas-egrn-hidden', !show); }
        }

        function setDialogError(message) {
            var $error = $dialogBody.find('.vas-egrn-error');
            $error.text(message || '').toggleClass('vas-egrn-hidden', !message);
        }

        function updateGenerateState() {
            $dialogBody.find('.vas-egrn-create-btn').prop('disabled', selectedOrderLineIDs.length === 0);
        }

        function fieldHtml(label, value, strong) {
            var shown = value == null || value === "" ? "-" : value;
            return '<div class="vas-egrn-field">' +
                '<div class="vas-egrn-field-lbl">' + escapeHtml(label) + '</div>' +
                '<div class="vas-egrn-field-val' + (strong ? ' strong' : '') + '" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
                '</div>';
        }

        function openOrderDialog(order) {
            if (!$dialog || !order) { return; }

            currentOrder = order;
            selectedOrderLineIDs = [];

            var childRecords = childRecordsMap[order.docNo] || [];

            $dialogTitle.text(order.docNo || '');
            $dialogBadge
                .removeClass('vas-egrn-hidden')
                .html('<span class="vas-egrn-pill info">' + escapeHtml(lbl("VAS_NoOfLines", "No of Lines") + ' ' + childRecords.length) + '</span>');

            var fields =
                '<div class="vas-egrn-form-grid">' +
                fieldHtml(lbl("VAS_PendingGRN", "Pending GRN"), order.docNo, true) +
                fieldHtml(lbl("Vendor", "Supplier"), order.customerName) +
                fieldHtml(lbl("VAS_VendorLocation", "Address"), order.address) +
                fieldHtml(lbl("VAS_NoOfLines", "No of Lines"), String(childRecords.length)) +
                '</div>';

            var rows = '';
            for (var i = 0; i < childRecords.length; i++) {
                var line = childRecords[i];
                rows +=
                    '<div class="vas-egrn-rcv-line vas-pgrn-line">' +
                    '<label class="vas-egrn-rcv-name vas-pgrn-name" title="' + escapeHtml(line.ProductName) + '">' +
                    '<input type="checkbox" class="vas-pgrn-check" data-orderlineid="' + escapeHtml(line.C_OrderLine_ID) + '"/>' +
                    '<span>' + escapeHtml(line.ProductName) + '</span>' +
                    '</label>' +
                    '<div class="vas-egrn-rcv-po" title="' + escapeHtml(lbl("VAS_Attribute", "Attribute")) + '">' + escapeHtml(line.AttributeName || '-') + '</div>' +
                    '<div class="vas-egrn-rcv-po" title="' + escapeHtml(lbl("VAS_RemianingQty", "Remaining Qty")) + '">' + escapeHtml(line.QtyEntered) + '</div>' +
                    '<div class="vas-egrn-rcv-uom" title="' + escapeHtml(line.UOM) + '">' + escapeHtml(line.UOM) + '</div>' +
                    '</div>';
            }

            var body;
            if (childRecords.length === 0) {
                body = '<div class="vas-egrn-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div>';
            } else {
                body =
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
                    '<div class="vas-egrn-lines">' + rows + '</div>' +
                    '<div class="vas-egrn-error vas-egrn-hidden"></div>' +
                    '<div class="vas-egrn-action"><button type="button" class="vas-egrn-create-btn" disabled>' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>' +
                    '<span>' + escapeHtml(lbl("VAS_GenerateGRN", "Generate GRN")) + '</span>' +
                    '</button></div>';
            }

            $dialogBody.html(body);
            $dialog.removeClass('vas-egrn-hidden');
            $('body').addClass('vas-egrn-body-lock');
        }

        function generateGRN(orderId) {
            if (selectedOrderLineIDs.length === 0) { return; }

            showDialogBusy(true);
            setDialogError('');
            $dialogBody.find('.vas-egrn-create-btn').prop('disabled', true);

            $.ajax({
                url: VIS.Application.contextUrl + "Product/CreateGRN",
                data: { C_Order_ID: orderId, C_OrderLines_IDs: selectedOrderLineIDs.join(',') },
                dataType: 'json',
                success: function (response) {
                    var data = JSON.parse(response);
                    showDialogBusy(false);
                    if (data.Shipment_ID > 0) {
                        closeDialog();
                        try {
                            if (AD_Window_ID > 0) {
                                var windowParam = {
                                    "TabWhereClause": "M_InOut.M_InOut_ID=" + data.Shipment_ID + "",
                                    "TabLayout": "Y",  // 'N'[Grid],'Y'[Single],'C'[Card]
                                    "TabIndex": "0"
                                };
                                $self.widgetFirevalueChanged(windowParam);
                            }
                        }
                        catch (e) {
                            console.log(e);
                        }
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

            // Append the pagination controls to the container
            $paginationContainer.append($pagination);

        }


        this.getRoot = function () {
            return $root;
        };

        /* This function is used to refresh the widget data */
        this.refreshWidget = function () {
            chartInstance = null;
            $self.currentPage = 1;
            $self.totalPages = 0;
            $self.intialLoad($self.currentPage);

        };

        this.disposeComponent = function () {
            if (rowResizeObserver) {
                rowResizeObserver.disconnect();
                rowResizeObserver = null;
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
