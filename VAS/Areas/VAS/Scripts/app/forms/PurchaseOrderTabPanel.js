﻿/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get Order Tax Data
 * chronological  : Development
 * Created Date   : 16 Feb 2024
 * Created by     : VAI051
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {
    VAS.PurchaseOrderTabPanel = function () {

        this.windowNo;
        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var ctx = this.ctx;
        var self = this;
        var lblTitle = null;
        var elements = [
            "VAS_OrderTitleTabPanel",
            "VAS_OrderTax",
            "VAS_DocumentNo",
            "VAS_OrderTaxPaybaleAmt",
            "VAS_OrderTaxAmt",
            "VAS_IsIncludeTax",
        ];

        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);
        var $root = $('<div class = "root"></div>');
        var wrapperDiv = $("<div class ='vas-apTaxWrapper'></div>")
        lblTitle = $("<h6 class='vas-apTax-ttl'>" + VAS.translatedTexts.VAS_OrderTitleTabPanel + "</h6>");

        this.init = function () {
            wrapperDiv.append(lblTitle);
            OrderTaxPanel();
            $root.append(wrapperDiv);
        };

        //Function defined to append design
        function OrderTaxPanel() {
            wrapperDiv.append(
                '<div class="vas-aptaxListGroup">' +
                '<div id="VAS-TaxDetail_' + self.windowNo + '" class= "VAS-TaxDetail mb-2" > ' +
                '</div>' +
                '</div>');
        };

        /*This function is used to get Order tax data*/
        this.getOrderTaxData = function (recordID) {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/PoReceipt/GetPurchaseOrderTaxData",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { OrderId: recordID },
                complete: function () {
                    if ($root.closest('.vis-ad-w-p-actionpanel-b').length > 0) {
                        $root.closest('.vis-ad-w-p-ap-tp-outerwrap').css('height', '100%');
                    }
                },
                success: function (data) {
                    wrapperDiv.find('#VAS-TaxDetail_' + self.windowNo).empty();
                    data = JSON.parse(data);
                    if (data != null && data.length > 0) {
                        for (i = 0; i < data.length; i++) {
                            var TabPaneldesign = '<div class="vas-apListItem mb-2">' +
                                '<div class="vas-ap-sglItem mb-2">' +
                                '<div class="vas-singleTaxElement vas-setTaxWidth1">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_DocumentNo + '</span>' +
                                '<span class="vas-singleTaxElementValue">' + data[i].DocumentNo + '</span>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setTaxWidth1">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_OrderTax + '</span>' +
                                '<span class="vas-singleTaxElementValue">' + data[i].TaxName + '</span>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setTaxAmtWidth1">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_OrderTaxAmt + '</span>' +
                                '<span class="vas-singleTaxElementValue">' + (data[i].TaxAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].stdPrecision, maximumFractionDigits: data[i].stdPrecision }) + '</span>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setTaxPaybleAmtWidth1">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_OrderTaxPaybaleAmt + '</span>' +
                                '<span class="vas-singleTaxElementValue">' + (data[i].TaxPaybleAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].stdPrecision, maximumFractionDigits: data[i].stdPrecision }) + '</span>' +
                                '</div>' +
                                '<div class="vas-apItem-checkbox d-flex align-items-center vas-setTaxWidthCheckbox ">' +
                                '<input type="checkbox" id="vas-includeTaxCheckbox" ' + (data[i].IsTaxIncluded == "Y" ? 'checked' : '') + ' disabled>' +
                                '<label class="vas-apcheckbox-label ml-1" for="vas-includeTaxCheckbox">' + VAS.translatedTexts.VAS_IsIncludeTax + '</label>' +
                                '</div>' +
                                '</div >' +

                                '</div>'
                            //Appending design to wrapperDiv
                            wrapperDiv.find('#VAS-TaxDetail_' + self.windowNo).append(TabPaneldesign);
                        }
                    }
                },
                error: function (eror) {
                    console.log(eror);
                }
            })
        }

        this.getRoot = function () {
            return $root;
        };

        VAS.PurchaseOrderTabPanel.prototype.startPanel = function (windowNo, curTab) {
            self.windowNo = windowNo;
            self.curTab = curTab;
            self.init();
        };

        /* This function to update tab panel based on selected record */
        VAS.PurchaseOrderTabPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getOrderTaxData(recordID);
        };

        /* This will set width as per window width in dafault case it is 75% */
        VAS.PurchaseOrderTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };

        /* Disposing all variables from memory */
        VAS.PurchaseOrderTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
            lblTitle = null;
        }
    }
})(VAS, jQuery);