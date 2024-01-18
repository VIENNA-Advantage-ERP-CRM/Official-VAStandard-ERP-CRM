/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get Invoice Tax Data
 * chronological  : Development
 * Created Date   : 16 January 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {


    VAS.InvoiceTaxTabPanel = function () {

        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var lblTitle = null;
        var ctx = this.ctx;

        var elements = [
            "VAS_InvoiceTaxTitleTabPanel",
            "VAS_InvoiceTax",
            "VAS_InvoiceTaxPaybaleAmt",
            "VAS_InvoiceTaxAmt",
            "VAS_IsIncludeTax",
        ];

        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);

        var $root = $('<div class = "root"></div>');
        var wrapperDiv = $("<div class ='vas-apTaxWrapper'></div>")
        lblTitle = $("<h6 class='vas-apTax-ttl'>" + VAS.translatedTexts.VAS_InvoiceTaxTitleTabPanel + "</h6>");
        this.init = function () {
            wrapperDiv.append(lblTitle);
            InvoiceTaxPanel();
            $root.append(wrapperDiv);
        };
        //Function defined to append design
        function InvoiceTaxPanel() {
            wrapperDiv.append(
                '<div class="vas-aptaxListGroup">' +
                '<div id="VAS-TaxDetail_' + this.windowNo + '" class= "VAS-TaxDetail mb-2" > ' +
                '</div>' +
                '</div>');
        };
        /*This function is used to get invoice tax data*/
        this.getInvoiceTaxData = function (recordID) {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/PoReceipt/GetTaxData",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { InvoiceId: recordID },
                success: function (data) {
                    if (JSON.parse(data) != "") {
                        data = JSON.parse(data);
                        console.log(data);
                        if (data != null && data.length > 0) {
                            for (i = 0; i < data.length; i++) {
                                var TabPaneldesign = '<div class="vas-apListItem mb-2">' +
                                    '<div class="vas-ap-sglItem mb-2">' +
                                    '<div class="vas-singleTaxElement vas-setTaxWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_InvoiceTax + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].TaxName + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxPaybleAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_InvoiceTaxPaybaleAmt + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + (data[i].TaxPaybleAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].stdPrecision, maximumFractionDigits: data[i].stdPrecision }) + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_InvoiceTaxAmt + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + (data[i].TaxAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].stdPrecision, maximumFractionDigits: data[i].stdPrecision }) + '</span>' +
                                    '</div>' +
                                    '</div >' +
                                    '<div class="vas-apItem-checkbox d-flex align-items-center">' +
                                    '<input type="checkbox" id="vas-includeTaxCheckbox" ' + (data[i].IsTaxIncluded == "Y" ? 'checked' : '') + '>' +
                                    '<label class="vas-apcheckbox-label ml-1" for="vas-includeTaxCheckbox">' + VAS.translatedTexts.VAS_IsIncludeTax + '</label>' +
                                    '</div>' +
                                    '</div>'
                                 //Appending design to wrapperDiv
                                 wrapperDiv.find('#VAS-TaxDetail_' + this.windowNo).append(TabPaneldesign);
                            }

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

        VAS.InvoiceTaxTabPanel.prototype.startPanel = function (windowNo, curTab) {
            this.windowNo = windowNo;
            this.curTab = curTab;
            this.init();
        };
        /* This function to update tab panel based on selected record */
        VAS.InvoiceTaxTabPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getInvoiceTaxData(recordID);
        };
        /* This will set width as per window width in dafault case it is 75% */
        VAS.InvoiceTaxTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };
        /* Disposing all variables from memory */
        VAS.InvoiceTaxTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
            lblTitle = null;
        }
    }
})(VAS, jQuery);