/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get PO History  Data
 * chronological  : Development
 * Created Date   : 01 July 2024
 * Created by     : VAI050
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {


    VAS.VAS_PurchaseOrderTabPanel = function () {
        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var lblTitle = null;
        var ctx = this.ctx;
        var self = this;

        var elements = [
            "VAS_QtyOrderedPO",
            "VAS_QtyDeliveredPO",
            "VAS_QtyInvoicedPO",
            "VAS_ProductsPO"
        ];

        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);
        var $root = $('<div class = "VAS-PO-summary h-100"></div>');
        var headerLabel = $('<div class="VAS-polist-heading">' + VAS.translatedTexts.VAS_ProductsPO + '</div>');
        var wrapperDiv = $("<div class ='VAS-PO-list-col'></div>")

        /*This function is used to get PO History  data*/
        this.getProductPanel = function (recordID) {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/PoReceipt/GetPOLineData",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { OrderID: recordID },
                complete: function () {
                    if ($root.closest('.vis-ad-w-p-actionpanel-b').length > 0) {
                        $root.closest('.vis-ad-w-p-ap-tp-outerwrap').css('height', '100%');
                    }
                },
                success: function (data) {
                    wrapperDiv.empty();
                    data = JSON.parse(data);
                    if (data != null && data.length > 0) {
                        if (lblTitle != null) {
                            lblTitle.remove();
                        }
                        if (data[0].OrderStatusValue == 'DE' || data[0].OrderStatusValue == 'DI') {
                            lblTitle = $('<div class="VAS-delivered-msg"><i class="fa fa-check-circle" aria-hidden="true"></i><span class="">' + data[0].OrderStatus + '</span> </div>');
                        }
                        else if (data[0].OrderStatusValue === 'OP' || data[0].OrderStatusValue === "") {

                            lblTitle = $('<div class="VAS-Open-msg"><span class="vis vis-info"></span>' + data[0].OrderStatus + '</div>');
                        }
                        else {
                            lblTitle = $('<div class="VAS-partially-msg"><span class="vis vis-info"></span>' + data[0].OrderStatus + '</div>');
                        }
                        $root.append(lblTitle).append(headerLabel);
                        for (i = 0; i < data.length; i++) {
                            var productDesign = '<div class="VAS-polist-box">';
                            if (data[i].OrderLineStatusValue === 'OP' || data[i].StatusValue === "") {
                                productDesign += '<div class="VAS-po-info-icon"><span class="vis vis-info vas-openItem"></span></div>';
                            }
                            else if (data[i].StatusValu && data[i].OrderLineStatusValue.Contains('P')) {
                                productDesign += '<div class="VAS-po-info-icon"><span class="vis vis-info"></span></div>';
                            }
                            else {
                                productDesign += '<div class="VAS-po-approve-icon"><i class="fa fa-check-circle" aria-hidden="true"></i></div>';
                            }
                            var imag = data[i].ImageUrl;
                            if (imag != "") {
                                imag = imag.substring(imag.lastIndexOf("/") + 1, imag.length);
                                var d = new Date();
                                productDesign += '<div class="VAS-product-thumbnail"><img alt="" title="" style="opacity:1;" src="' + VIS.Application.contextUrl + "Images/Thumb46x46/" + imag + "?" + d.getTime() + '"></div>';
                            }
                            else {
                                productDesign += '<div class="VAS-product-thumbnail"><i class="vis vis-image"></i></div>';
                            }

                            productDesign += '<div class="VAS-PO-details">' +
                                '<h1>' + data[i].ProductName + '</h1>' +
                                '<div class="VAS-Attrbiute">' + (data[i].AttributeName == null ? "" : data[i].AttributeName) + '</div>' +
                                '<div class="VAS-UPC-num">' + ((data[i].UPC == null || data[i].UPC == "") ? "" : data[i].UPC + ' | ') + '<span>' + data[i].UOM + '</span></div>' +
                                '<div class="VAS-order-description">' +
                                '<div class="VAS-order-values"><h6>' + VAS.translatedTexts.VAS_QtyOrderedPO + '</h6><span>' + data[i].QtyOrdered + '</span></div>' +
                                '<div class="VAS-order-values"><h6>' + VAS.translatedTexts.VAS_QtyDeliveredPO + '</h6><span>' + data[i].QtyDelivered + '</span></div>' +
                                /* '<div class="VAS-order-values"><h6>' + VAS.translatedTexts.VAS_QtyInvoicedPO + '</h6><span>' + data[i].QtyInvoiced + '</span></div>' +*/
                                '</div>' +
                                '</div>' +
                                '</div>';

                            //Appending design to wrapperDiv
                            wrapperDiv.append(productDesign);
                        }
                    }
                    $root.append(wrapperDiv);
                },
                error: function (eror) {
                    console.log(eror);
                }
            })
        };

        this.getRoot = function () {
            return $root;
        };

        VAS.VAS_PurchaseOrderTabPanel.prototype.startPanel = function (windowNo, curTab) {
            self.windowNo = windowNo;
            self.curTab = curTab;
        };

        /* This function to update tab panel based on selected record */
        VAS.VAS_PurchaseOrderTabPanel.prototype.refreshPanelData = function (recordID, selectedRow, windowEvt) {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getProductPanel(recordID);
        };

        /* This will set width as per window width in dafault case it is 75% */
        VAS.VAS_PurchaseOrderTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };

        /* Disposing all variables from memory */
        VAS.VAS_PurchaseOrderTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
            lblTitle = null;
        }
    }
})(VAS, jQuery);