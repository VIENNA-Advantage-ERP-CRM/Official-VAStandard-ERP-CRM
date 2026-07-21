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
                '        <div id="VAS_ProductDetail_' + widgetID + '" class="VAS-deliveries-listing"></div>' +
                '    </div>' +
                '    <div class="vas-egrn-foot">' +
                '        <span class="vas-egrn-foot-info" id="VAS_FootInfo_' + widgetID + '"></span>' +
                '        <div class="VAS-pagination-container"></div>' +
                '    </div>' +
                '</div>';
            // Create busy indicator
            createBusyIndicator();

            $root.append(orderContainer);
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
                                ' data-doc-no="' + order["DocumentNo"] + '"' +
                                ' data-customer-name="' + order["CustomerName"] + '"' +
                                ' data-orderid="' + order["C_Order_ID"] + '">' +
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
                            var docNo = $(this).data('doc-no');
                            var customerName = $(this).data('customer-name');
                            var orderid = $(this).data('orderid');
                            displayOrderDetails(docNo, customerName, orderid);
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

        function displayOrderDetails(docNo, customerName, orderid) {
            // Hide and remove existing elements
            $root.find('#VAS_DeliveryContainer_' + widgetID).hide();
            $root.find('#VAS_ProductContainer_' + widgetID).remove();

            // Initialize the selected order line IDs array
            selectedOrderLineIDs = [];

            var productContainer =
                '<div id="VAS_ProductContainer_' + widgetID + '" class="VAS-grn-container-pending">' +
                '<span class="VAS-info-span" style="display:none;" id="VAS_spnErrorMessage_' + widgetID + '"></span>' +
                '    <div class="VAS-deliveries-heading">' +
                '        <h6>' +
                '            <span id="VAS_BackTodelivery_' + widgetID + '" class="vis vis-arrow-left VAS-pointer-cursor"></span>' +
                '            ' + VIS.Msg.getMsg("VAS_BackToGRN") +
                '        </h6>' +
                '<span id="VAS_GenerateGRN_' + widgetID + '" class="VAS-generate-delivery-btn" data-orderid="' + orderid + '" title="' + VIS.Msg.getMsg("VAS_GenerateGRN") + '">' +
                '<i class="vis vis-action" ></i ></span> ' +
                '    </div>' +
                '    <div class="VAS-delivery-count">' +
                '    </div>' +
                '    <div class="VAS-delivery-detail">' +
                '        <div class="VAS-box-heading VAS-orders-text-white">' +
                '            <div class="VAS-expected-col">' +
                '                <div class="VAS-icon-w-name">' +
                '                    <i class="fa fa-file-text VAS-orders-text-white" aria-hidden="true"></i>' +
                '                    <div class="VAS-doc-no VAS-orders-text-white" title="' + VIS.Msg.getMsg("Document_No") + '">' + docNo + '</div>' +
                '                </div>' +
                '                <div class="VAS-expectedTxt" title="' + VIS.Msg.getMsg("Vendor") + '">' + customerName + '</div>' +
                '            </div>' +
                '            <div class="VAS-dty-prod">' +
                '                <div class="VAS-qtyProd-text">' + VIS.Msg.getMsg("VAS_NoOfLines") + '</div>' +
                '                <div class="VAS-total-items-count VAS-orders-text-white"><span id="VAS_TotalQty_' + widgetID + '"></span></div>' +
                '            </div>' +
                '        </div>' +
                '    </div>' +
                '    <div id="VAS_ProductDiv_' + widgetID + '" class="VAS-height-container">' +
                '        <div id="VAS_OrderLine_' + widgetID + '" class="VAS-deliveries-listing"></div>' +
                '</div>' +
                '<div id="VAS_OrderLinePagination_' + widgetID + '" class="VAS-pagination-container"></div>';

            $root.append(productContainer);

            // Fetch child records based on the clicked document number
            var childRecords = childRecordsMap[docNo] || [];
            var itemsPerPage = 6;
            var totalPages = Math.ceil(childRecords.length / itemsPerPage);
            var currentPage = 1;

            function updatePage(page) {
                // Ensure page is within bounds
                if (page < 1) {
                    page = 1;
                }
                if (page > totalPages) {
                    page = totalPages;
                }
                currentPage = page;
                // Calculate start and end index
                var startIndex = (currentPage - 1) * itemsPerPage;
                var endIndex = Math.min(startIndex + itemsPerPage, childRecords.length);

                // Clear previous records
                $root.find('#VAS_OrderLine_' + widgetID).empty();
                // Generate HTML for records of the current page
                for (var i = startIndex; i < endIndex; i++) {
                    var line = childRecords[i];
                    var isChecked = selectedOrderLineIDs.includes(line.C_OrderLine_ID);
                    //var hasStock = line.OnHandQty > 0 && line.OnHandQty >= line.QtyOrdered;
                    //var hasStock = line.OnHandQty > 0;
                    //var boxClass = hasStock ? 'VAS-delivery-box' : 'VAS-delivery-box no-stock';

                    $root.find('#VAS_OrderLine_' + widgetID).append(
                        '            <div class="VAS-delivery-box">' +
                        '                <div class="VAS-box-heading">' +
                        '                    <div class="VAS-icon-w-name">' +
                        '                        <input type="checkbox" class="VAS-selection-checkbox" data-orderlineid="' + line.C_OrderLine_ID + '"' + (isChecked ? ' checked' : '') + '/> ' +
                        '                        <i class="fa fa-file-text" aria-hidden="true"></i>' +
                        '                        <div class="VAS-doc-no" title="' + VIS.Msg.getMsg("VAS_Product") + '">' + line.ProductName + '</div>' +
                        '                    </div>' +
                        '                    <div class="VAS-total-items-count"><span title="' + VIS.Msg.getMsg("VAS_RemianingQty") + '">' + line.QtyEntered + '</span></div>' +
                        '                </div>' +
                        '                <div class="VAS-spaceBetween-col">' +
                        '                    <div class="VAS-lbl-text" title="' + VIS.Msg.getMsg("VAS_Attribute") + '">' + line.AttributeName + '</div>' +
                        '                    <div class="vas-lbl-text" title="' + VIS.Msg.getMsg("VAS_Uom") + '"> ' + line.UOM + '</div>' +
                        '                </div>' +
                        '            </div>');
                }
                $root.find('#VAS_TotalQty_' + widgetID).text(childRecords.length);
                /*  Append pagination controls*/
                $root.find('#VAS_OrderLinePagination_' + widgetID).empty();
                $root.find('#VAS_OrderLinePagination_' + widgetID).append(
                    '        <div class="VAS-slider-arrows-order-details VAS-orders-text-white">' +
                    '            <i class="fa fa-arrow-circle-left" aria-hidden="true" id="VAS_PreviousPage_' + widgetID + '"></i>' +
                    '            <span>' + currentPage + VIS.Msg.getMsg("VAS_Of") + totalPages + '</span>' +
                    '            <i class="fa fa-arrow-circle-right" aria-hidden="true" id="VAS_NextPage_' + widgetID + '"></i>' +
                    '        </div>');
            }

            // Initialize first page
            if (childRecords.length > 0) {
                updatePage(currentPage);
            }

            // Unbind any previously bound event handlers
            $root.off('change', '.VAS-selection-checkbox');
            $root.off('click', '#VAS_PreviousPage_' + widgetID);
            $root.off('click', '#VAS_NextPage_' + widgetID);
            $root.off('click', '#VAS_BackTodelivery_' + widgetID);
            $root.off('click', '#VAS_GenerateGRN_' + widgetID);

            // Event listeners for pagination controls
            $root.on('click', '#VAS_PreviousPage_' + widgetID, function () {
                updatePage(currentPage - 1);
            });

            $root.on('click', '#VAS_NextPage_' + widgetID, function () {
                updatePage(currentPage + 1);
            });

            // Back to deliveries
            $root.on('click', '#VAS_BackTodelivery_' + widgetID, function () {
                $root.find('#VAS_DeliveryContainer_' + widgetID).show();
                $root.find('#VAS_ProductContainer_' + widgetID).remove();
                $root.find('.VAS-error-message').remove();
            });

            // Event listener for checkbox selection
            $root.on('change', '.VAS-selection-checkbox', function () {
                var orderlineID = $(this).data('orderlineid');
                if ($(this).is(':checked')) {
                    // Add ID to array if checked
                    if (!selectedOrderLineIDs.includes(orderlineID)) {
                        selectedOrderLineIDs.push(orderlineID);
                    }
                }
                else {
                    // Remove ID from array if unchecked
                    selectedOrderLineIDs = selectedOrderLineIDs.filter(id => id !== orderlineID);
                }

                if (selectedOrderLineIDs.length > 0) {
                    $root.find('#VAS_GenerateGRN_' + widgetID).show();
                } else {
                    $root.find('#VAS_GenerateGRN_' + widgetID).hide();
                }
                console.log(selectedOrderLineIDs);
            });

            // Event listener for Generate Delivery Order button
            $root.on('click', '#VAS_GenerateGRN_' + widgetID, function () {
                var orderId = $(this).data('orderid');
                generateGRN(orderId);
            });


            $root.find('#VAS_GenerateGRN_' + widgetID).hide();

            function generateGRN(orderId) {
                $bsyDiv.css('visibility', 'visible');
                var orderLineIDs = selectedOrderLineIDs.join(',');
                $.ajax({
                    url: VIS.Application.contextUrl + "Product/CreateGRN",
                    data: { C_Order_ID: orderId, C_OrderLines_IDs: orderLineIDs },
                    dataType: 'json',
                    success: function (response) {
                        var response = JSON.parse(response);
                        if (response.Shipment_ID > 0) {
                            try {
                                if (AD_Window_ID > 0) {
                                    var windowParam = {
                                        "TabWhereClause": "M_InOut.M_InOut_ID=" + response.Shipment_ID + "",
                                        "TabLayout": "Y",  // 'N'[Grid],'Y'[Single],'C'[Card]}	 	 
                                        "TabIndex": "0",
                                    }
                                    $self.widgetFirevalueChanged(windowParam);
                                    $self.currentPage = 1;
                                    $self.intialLoad($self.currentPage);
                                }
                            }
                            catch (e) {
                                console.log(e);
                            }
                        }
                        else {

                            var spnWO = $root.find('#VAS_spnErrorMessage_' + widgetID);
                            var message = "";
                            if (response.message != null && response.message != "") {
                                message = response.message;
                            }
                            else {
                                message = VIS.Msg.getMsg("VAS_DeliveryOrderNotGenerated");
                            }
                            spnWO.text(message);
                            spnWO.fadeIn();
                            spnWO.fadeOut(5000);

                        }
                        $bsyDiv.css('visibility', 'hidden');

                    },
                    error: function (xhr, status, error) {
                        // Handle errors
                        console.log('Failed to fetch data:', status, error);
                        $bsyDiv[0].style.visibility = "hidden";
                    }
                });

                console.log('Generating delivery order for ID:', orderId);
            }

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
