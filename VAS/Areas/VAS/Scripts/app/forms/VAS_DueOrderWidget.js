/************************************************************
 * Module Name    : VAS
 * Purpose        :Get the details of PO and Create GRN
 * chronological  : Development
 * Created Date   : 20 Sep 2024
 * Created by     : VAI050
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_DueOrderWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100">'); // Root container
        this.currentPage = 1;
        this.totalPages = 0;
        var widgetID = 0;
        // Create a map to store child records by document number
        var childRecordsMap = {};
        var pageSize = 4;
        var selectedOrderLineIDs = []; // Array to keep track of selected order line IDs
        var AD_Window_ID = 0;
        this.initalize = function () {
            widgetID = this.widgetInfo.AD_UserHomeWidgetID;
            const orderContainer =
                '<div id="VAS_DeliveryContainer_' + widgetID + '" class="VAS-due-fulfilment-container">' +
                '    <div class="VAS-orders-heading">' +
                '        <h6>' + VIS.Msg.getMsg("VAS_DueOrders") + '</h6>' +
                '    </div>' +
                '    <div class="VAS-delivery-count">' +
                '        <div class="VAS-count-lbl">' + VIS.Msg.getMsg("VAS_Orders") + ' <span id="VAS_DeliveryCount_' + widgetID + '">0</span></div>' +
                '    </div>' +
                '    <div class="VAS-delivery-detail">' +
                '        <div class="VAS-box-heading">' +
                '        </div>' +
                '    </div>' +
                '    <div class="VAS-height-container">' +
                '        <div id="VAS_DeliveryBox_' + widgetID + '" class="VAS-deliveries-listing">' +
                '        </div>' +
                '        <div id="VAS_ProductDetail_' + widgetID + '" class="VAS-deliveries-listing">' +
                '        </div>' +
                '    </div>' +
                '<div class="VAS-pagination-container"></div>' +
                '</div>';
            // Create busy indicator
            createBusyIndicator();

            $root.append(orderContainer);
            //    buildPagination();
        };


        /* This function will load data in widget */
        this.intialLoad = function (pageNo) {
            // Show busy indicator
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
                        for (i = 0; i < response.Orders.length; i++) {
                            var boxHtml = ('<div id="VAS_OrderContainer_' + widgetID + '" class="VAS-delivery-box" >' +
                                '    <div class="VAS-box-heading">' +
                                '        <div class="VAS-icon-w-name">' +
                                '            <i class="fa fa-file-text" aria-hidden="true"></i>' +
                                '            <div id="VAS_DocumentNo_' + widgetID + '" class="VAS-doc-no VAS-pointer-cursor" ' +
                                '                title="' + VIS.Msg.getMsg("Document_No") + '" ' +
                                '                data-doc-no="' + response.Orders[i]["DocumentNo"] + '" ' +
                                '                data-customer-name="' + response.Orders[i]["CustomerName"] + '" ' +
                                '                data-orderid="' + response.Orders[i]["C_Order_ID"] + '">' + // Add data-orderid attribute here
                                '                ' + response.Orders[i]["DocumentNo"] +
                                '            </div>' +
                                '        </div>' +
                                '        <div class="VAS-total-items-count" title="' + VIS.Msg.getMsg("VAS_NoOfLines") + '">' + response.Orders[i]["LineCount"] + '</div>' +
                                '    </div>' +
                                '    <div class="VAS-spaceBetween-col">' +
                                '        <div class="VAS-lbl-text" title="' + VIS.Msg.getMsg("Vendor") + '">' + response.Orders[i]["CustomerName"] + '</div>' +
                                '    </div>' +
                                '    <div class="VAS-spaceBetween-col grid-2-col">' +
                                '        <div class="VAS-lbl-text" title="' + VIS.Msg.getMsg("VAS_VendorLocation") + '">' + response.Orders[i]["DeliveryLocation"] + '</div>' +
                                '        <div class="VAS-lbl-text text-right" title="' + VIS.Msg.getMsg("VAS_ProductLocation") + '">' + response.Orders[i]["ProductLocation"] + '</div>' +
                                '    </div>' +
                                '    <div class="VAS-spaceBetween-col">' +
                                '        <div class="VAS-lbl-text" title="' + VIS.Msg.getMsg("TotalAmount") + '"><span>' + response.Orders[i]["Symbol"] + '</span>' + ' ' + (response.Orders[i]["GrandTotal"]).toLocaleString() + '</div>' +
                                '    </div>' +
                                '</div>');
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
                        if (pageNo == 1) {
                            $root.find('#VAS_DeliveryCount_' + widgetID).text(response.RecordCount);
                            buildPagination(response.RecordCount);
                            AD_Window_ID = response.AD_Window_ID;
                        }
                        $root.find('#VAS_PaginationText_' + widgetID).text($self.currentPage + VIS.Msg.getMsg("VAS_Of") + $self.totalPages);
                        // Attach click event listener to delivery boxes
                        $root.off('click', '#VAS_DocumentNo_' + widgetID);
                        $root.on('click', '#VAS_DocumentNo_' + widgetID, function () {
                            var docNo = $(this).data('doc-no');
                            var customerName = $(this).data('customer-name');
                            var orderid = $(this).data('orderid');
                            displayOrderDetails(docNo, customerName, orderid);
                        });
                    }
                    //else {

                    //    // Display "No data available" message
                    //    const message = $('<div class="VAS-data-message">' + VIS.Msg.getMsg("VAS_NoDataAvailable") + '</div>');
                    //    $root.find('.VAS-height-container').append(message);
                    //}
                    $bsyDiv.css('visibility', 'hidden');

                },
                error: function (xhr, status, error) {
                    // Handle errors
                    console.log('Failed to fetch data:', status, error);
                    $bsyDiv[0].style.visibility = "hidden";
                }
            });
        };

        function displayOrderDetails(docNo, customerName, orderid) {
            // Hide and remove existing elements
            $root.find('#VAS_DeliveryContainer_' + widgetID).hide();
            $root.find('#VAS_ProductContainer_' + widgetID).remove();

            // Initialize the selected order line IDs array
            selectedOrderLineIDs = [];

            var productContainer =
                '<div id="VAS_ProductContainer_' + widgetID + '" class="VAS-due-fulfilment-container">' +
                '<span class="VAS-info-span" style="display:none;" id="VAS_spnErrorMessage_' + widgetID + '"></span>' +
                '    <div class="VAS-orders-heading">' +
                '        <h6>' +
                '            <span id="VAS_BackTodelivery_' + widgetID + '" class="vis vis-arrow-left VAS-pointer-cursor"></span>' +
                '            ' + VIS.Msg.getMsg("VAS_BackToOrder") +
                '        </h6>' +
                '<span id="VAS_GenerateGRN_' + widgetID + '" class="VAS-generate-delivery-btn" data-orderid="' + orderid + '" title="' + VIS.Msg.getMsg("VAS_GenerateGRN") + '">' +
                '<i class="vis vis-action" ></i ></span> ' +
                '    </div>' +
                '    <div class="VAS-delivery-count">' +
                '    </div>' +
                '    <div class="VAS-delivery-detail">' +
                '        <div class="VAS-box-heading">' +
                '            <div class="VAS-expected-col">' +
                '                <div class="VAS-icon-w-name">' +
                '                    <i class="fa fa-file-text" aria-hidden="true"></i>' +
                '                    <div class="VAS-doc-no" title="' + VIS.Msg.getMsg("Document_No") + '">' + docNo + '</div>' +
                '                </div>' +
                '                <div class="VAS-expectedTxt" title="' + VIS.Msg.getMsg("Vendor") + '">' + customerName + '</div>' +
                '            </div>' +
                '            <div class="VAS-dty-prod">' +
                '                <div class="VAS-qtyProd-text">' + VIS.Msg.getMsg("VAS_NoOfLines") + '</div>' +
                '                <div class="VAS-total-items-count"><span id="VAS_TotalQty_' + widgetID + '"></span></div>' +
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
                    '        <div class="VAS-slider-arrows-order-details">' +
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
                            $self.currentPage = 1;
                            $self.intialLoad($self.currentPage);
                            if (AD_Window_ID > 0) {
                                var zoomQuery = new VIS.Query();
                                zoomQuery.addRestriction("M_InOut_ID", VIS.Query.prototype.EQUAL, response.Shipment_ID);
                                zoomQuery.setRecordCount(1);
                                VIS.viewManager.startWindow(AD_Window_ID, zoomQuery);
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
            var $pagination = $('<div class="VAS-slider-arrows">' +
                '        <i id="VAS_Prev_Page_' + widgetID + '" class="fa fa-arrow-circle-left" aria-hidden="true"></i>' +
                '        <span id="VAS_PaginationText_' + widgetID + '">' + $self.currentPage + VIS.Msg.getMsg("VAS_Of") + $self.totalPages + '</span>' +
                '        <i id="VAS_Next_Page_' + widgetID + '" class="fa fa-arrow-circle-right" aria-hidden="true"></i>' +
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
    };

    VAS.VAS_DueOrderWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    VAS.VAS_DueOrderWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_DueOrderWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_DueOrderWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_DueOrderWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_DueOrderWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
        $contentContainer = null;
    };

})(VAS, jQuery);