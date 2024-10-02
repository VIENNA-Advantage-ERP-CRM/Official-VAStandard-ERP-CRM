/************************************************************
 * Module Name    : VAS
 * Purpose        : Get the Pending Material Transfer Details
 * chronological  : Development
 * Created Date   : 26 Sep 2024
 * Created by     : VIS0060
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_PendingTransferWidget = function () {
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
        var pageSize = 5;
        var selecteLineIDs = []; // Array to keep track of selected order line IDs

        this.initalize = function () {
            widgetID = this.widgetInfo.AD_UserHomeWidgetID;
            // Create busy indicator
            createBusyIndicator();
            var reqContainer = '<div id="VAS_TransferContainer_' + widgetID + '" class="VAS-deliveries-container-pending">' +
                            '<div class="VAS-deliveries-heading">' +
                            '<h6>' + VIS.Msg.getMsg("VAS_PendingTransfers") + '</h6>' +
                            '</div>' +
                            '<div class="VAS-delivery-count">' +
                            '<div class="VAS-count-lbl">' + VIS.Msg.getMsg("VAS_Transfers") + ' <span id="VAS_TransferCount_' + widgetID + '">0</span></div>' +
                            '</div>' +
                            '<div class="VAS-delivery-detail">' +
                            '<div class="VAS-box-heading">' +
                            '</div>' +
                            '</div>' +
                            '<div class="VAS-height-container">' +
                            '<div id="VAS_TransferBox_' + widgetID + '" class="VAS-deliveries-listing">' +
                            '</div>' +
                            '<div id="VAS_ProductDetail_' + widgetID + '" class="VAS-deliveries-listing">' +
                            '</div>' +
                            '</div>' +
                            '<div class="VAS-pagination-container"></div>' +
                            '</div>';
            $root.append(reqContainer);
        };

        /* This function will load data in widget */
        this.intialLoad = function (pageNo) {
            // Show busy indicator
            $bsyDiv.css('visibility', 'visible');
            $root.find('#VAS_ProductContainer_' + widgetID).remove();
            $root.find('#VAS_TransferContainer_' + widgetID).show();

            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "Product/GetExpectedTransfer",
                { pageNo: pageNo, pageSize: pageSize, Type: "PT" }, function (response) {
                    $root.find('#VAS_TransferBox_' + widgetID).empty();
                    $root.find('#VAS_ReqContainer' + widgetID).remove;
                    if (response != null && response.Requisitions != null && response.Requisitions.length > 0) {
                        for (i = 0; i < response.Requisitions.length; i++) {
                            var boxHtml = '<div id="VAS_ReqContainer' + widgetID + '" class="VAS-delivery-box" >' +
                                '<div class="VAS-box-heading">' +
                                '<div class="VAS-icon-w-name">' +
                                '<i class="fa fa-file-text" aria-hidden="true"></i>' +
                                '<div id="VAS_DocumentNo_' + widgetID + '" class="VAS-doc-no VAS-pointer-cursor" ' +
                                'title="' + VIS.Msg.getMsg("DocumentNo") + '" ' +
                                'data-doc-no="' + response.Requisitions[i]["DocumentNo"] + '" ' +
                                'data-customer-name="' + response.Requisitions[i]["Employee"] + '" ' +
                                'data-reqid="' + response.Requisitions[i]["M_Requisition_ID"] + '">' + // Add data-reqid attribute here
                                '' + response.Requisitions[i]["DocumentNo"] +
                                '</div>' +
                                '</div>' +
                                '<div class="VAS-total-items-count" title="' + VIS.Msg.getMsg("VAS_NoOfLines") + '">' + response.Requisitions[i]["LineCount"] + '</div>' +
                                '</div>' +
                                '<div class="VAS-spaceBetween-col">' +
                                // '<div class="VAS-lbl-text" title="' + VIS.Msg.getMsg("Employee") + '">' + response.Requisitions[i]["Employee"] + '</div>' +
                                '</div>' +
                                '<div class="VAS-spaceBetween-col grid-2-col">' +
                                '<div class="VAS-lbl-text" title="' + VIS.Msg.getMsg("VAS_Destination") + '">' + response.Requisitions[i]["Destination"] + '</div>' +
                                '<div class="VAS-lbl-text text-right" title="' + VIS.Msg.getMsg("VAS_Source") + '">' + response.Requisitions[i]["Source"] + '</div>' +
                                '</div>' +
                                '<div class="VAS-spaceBetween-col">' +
                                '<div class="VAS-lbl-text" title="' + VIS.Msg.getMsg("TotalAmount") + '"><span>' + response.Requisitions[i]["Symbol"] + '</span>' + ' ' + (response.Requisitions[i]["GrandTotal"]).toLocaleString() + '</div>' +
                                '</div>' +
                                '</div>';
                            $root.find('#VAS_TransferBox_' + widgetID).append(boxHtml);
                        }

                        childRecordsMap = [];
                        // Populate the childRecordsMap with child records
                        response.Requisitions.forEach(function (requisition) {
                            if (requisition.ReqLines && requisition.ReqLines.length > 0) {
                                childRecordsMap[requisition.DocumentNo] = requisition.ReqLines;
                            }
                        });

                        /* Add Pagination div on first tym data load*/
                        if (pageNo == 1) {
                            $root.find('#VAS_TransferCount_' + widgetID).text(response.RecordCount);
                            buildPagination(response.RecordCount);
                        }

                        $root.find('#VAS_PaginationText_' + widgetID).text($self.currentPage + VIS.Msg.getMsg("VAS_Of") + $self.totalPages);

                        // Attach click event listener to transfer boxes
                        $root.off('click', '#VAS_DocumentNo_' + widgetID);
                        $root.on('click', '#VAS_DocumentNo_' + widgetID, function () {
                            var docNo = $(this).data('doc-no');
                            var customerName = $(this).data('customer-name');
                            var reqid = $(this).data('reqid');
                            displayOrderDetails(docNo, customerName, reqid);
                        });
                    }
                    $bsyDiv.css('visibility', 'hidden');
                });
        };

        function displayOrderDetails(docNo, customerName, reqid) {
            // Hide and remove existing elements
            $root.find('#VAS_TransferContainer_' + widgetID).hide();
            $root.find('#VAS_ProductContainer_' + widgetID).remove();

            // Initialize the selected order line IDs array
            selecteLineIDs = [];

            var productContainer =
                '<div id="VAS_ProductContainer_' + widgetID + '" class="VAS-deliveries-container-pending">' +
                '<span class="VAS-info-span" style="display:none;" id="VAS_spnErrorMessage_' + widgetID + '"></span>' +
                '    <div class="VAS-deliveries-heading">' +
                '        <h6>' +
                '            <span id="VAS_BackToTransfer_' + widgetID + '" class="vis vis-arrow-left VAS-pointer-cursor"></span>' +
                '            ' + VIS.Msg.getMsg("VAS_BackToTransfer") +
                '        </h6>' +
                '<span id="VAS_GenerateTransfer_' + widgetID + '" class="VAS-generate-delivery-btn" data-reqid="' + reqid + '" title="' + VIS.Msg.getMsg("VAS_GenMaterialTransfer") + '"><i class="fa fa-truck" aria-hidden="true"></i></span>' +
                '    </div>' +
                '    <div class="VAS-delivery-count">' +
                '    </div>' +
                '    <div class="VAS-delivery-detail">' +
                '        <div class="VAS-box-heading">' +
                '            <div class="VAS-expected-col">' +
                '                <div class="VAS-icon-w-name">' +
                '                    <i class="fa fa-file-text" aria-hidden="true"></i>' +
                '                    <div class="VAS-doc-no">' + docNo + '</div>' +
                '                </div>' +
                //'                <div class="VAS-expectedTxt" title="' + VIS.Msg.getMsg("Employee") + '">' + customerName + '</div>' +
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
                    var isChecked = selecteLineIDs.includes(line.C_OrderLine_ID);
                    var hasStock = line.OnHandQty > 0 && line.OnHandQty >= line.QtyOrdered;
                    var boxClass = hasStock ? 'VAS-delivery-box' : 'VAS-delivery-box no-stock';
                    var badgeClass;
                    if (line.OnHandQty >= line.QtyOrdered) {
                        badgeClass = 'badge-green'; //  stock available
                    } else if (line.OnHandQty < line.QtyOrdered && line.OnHandQty > 0) {
                        badgeClass = 'badge-orange'; //stock less tham order 
                    } else {
                        badgeClass = 'badge-red'; // Insufficient stock

                    }

                    $root.find('#VAS_OrderLine_' + widgetID).append(
                        '            <div class="' + boxClass + '">' +
                        '                <div class="VAS-box-heading">' +
                        '                    <div class="VAS-icon-w-name">' +
                        '                        <input type="checkbox" class="VAS-selection-checkbox" data-reqlineid="' + line.M_RequisitionLine_ID + '"' + (isChecked ? ' checked' : '') + (hasStock ? '' : ' disabled') + '/> ' +
                        '                        <i class="fa fa-file-text" aria-hidden="true"></i>' +
                        '                        <div class="VAS-doc-no" title="' + VIS.Msg.getMsg("VAS_Product") + '">' + line.ProductName + '</div>' +
                        '                    </div>' +
                        '                    <div class="VAS-total-items-count">' +
                        '                    <span title = "' + VIS.Msg.getMsg("VAS_RemianingQty") + '" class="badge badge-light ' + badgeClass + '"> ' + line.QtyEntered + '</span ></div > ' +
                        '                      </div>' +
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
            $root.off('click', '#VAS_BackToTransfer_' + widgetID);
            $root.off('click', '#VAS_GenerateTransfer_' + widgetID);

            // Event listeners for pagination controls
            $root.on('click', '#VAS_PreviousPage_' + widgetID, function () {
                updatePage(currentPage - 1);
            });

            $root.on('click', '#VAS_NextPage_' + widgetID, function () {
                updatePage(currentPage + 1);
            });

            // Back to deliveries
            $root.on('click', '#VAS_BackToTransfer_' + widgetID, function () {
                $root.find('#VAS_TransferContainer_' + widgetID).show();
                $root.find('#VAS_ProductContainer_' + widgetID).remove();
                $root.find('.VAS-error-message').remove();
            });

            // Event listener for checkbox selection
            $root.on('change', '.VAS-selection-checkbox', function () {
                var reqlineid = $(this).data('reqlineid');
                if ($(this).is(':checked')) {
                    // Add ID to array if checked
                    if (!selecteLineIDs.includes(reqlineid)) {
                        selecteLineIDs.push(reqlineid);
                    }
                }
                else {
                    // Remove ID from array if unchecked
                    selecteLineIDs = selecteLineIDs.filter(id => id !== reqlineid);
                }

                if (selecteLineIDs.length > 0) {
                    $root.find('#VAS_GenerateTransfer_' + widgetID).show();
                } else {
                    $root.find('#VAS_GenerateTransfer_' + widgetID).hide();
                }
            });

            // Event listener for Generate Delivery Order button
            $root.on('click', '#VAS_GenerateTransfer_' + widgetID, function () {
                var reqId = $(this).data('reqid');
                generateMaterialTransfer(reqId);
            });

            $root.find('#VAS_GenerateTransfer_' + widgetID).hide();
        }

        function generateMaterialTransfer(reqId) {
            $bsyDiv.css('visibility', 'visible');
            var reqLineIDs = selecteLineIDs.join(',');
            $.ajax({
                url: VIS.Application.contextUrl + "Product/CreateMaterialTransfer",
                data: { M_Requisition_ID: reqId, M_RequisitionLines_IDs: reqLineIDs },
                dataType: 'json',
                success: function (response) {
                    var response = JSON.parse(response);
                    if (response.Movement_ID > 0) {
                        try {
                            var windowParam = {
                                "TabWhereClause": "M_Movement.M_Movement_ID=" + response.Movement_ID,
                                "TabLayout": "Y",  // 'N'[Grid],'Y'[Single],'C'[Card]}	 	 
                                "TabIndex": "0",
                            }
                            $self.widgetFirevalueChanged(windowParam);
                            $self.currentPage = 1;
                            $self.intialLoad($self.currentPage);
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
                            message = VIS.Msg.getMsg("VAS_TransferNotGenerated");
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

    VAS.VAS_PendingTransferWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    VAS.VAS_PendingTransferWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_PendingTransferWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_PendingTransferWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_PendingTransferWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_PendingTransferWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);