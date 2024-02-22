/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get Line History  Data
 * chronological  : Development
 * Created Date   : 20 Feb 2024
 * Created by     : VAI051
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {


    VAS.LineHistoryTabPanel = function () {

        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var lblTitle = null;
        var ctx = this.ctx;

        var elements = [
            "VAS_HistoryTitleTabPanel",
            "VAS_LineHistory",
            "VAS_LineNo",
            "VAS_DateOrdered",
            "VAS_DatePromised",
            "VAS_Product",
            "VAS_Charge",
            "VAS_Quantity",
            "VAS_UOM",
            "VAS_QuantityOrdered",
            "VAS_Price",
            "VAS_ListPrice",
            "VAS_UnitPrice",
            "VAS_Tax",
            "VAS_Discount",
            "VAS_LineAmount",
            "VAS_Description"
        ];

        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);

        var $root = $('<div class = "root"></div>');
        var wrapperDiv = $("<div class ='vas-apTaxWrapper'></div>")
        lblTitle = $("<h6 class='vas-apTax-ttl'>" + VAS.translatedTexts.VAS_LineHistory + "</h6>");

        this.init = function () {
            wrapperDiv.append(lblTitle);
            LineHistoryPanel();
            $root.append(wrapperDiv);
        };

        //Function defined to append design
        function LineHistoryPanel() {
            wrapperDiv.append(
                '<div class="vas-aptaxListGroup">' +
                '<div id="VAS-TaxDetail_' + this.windowNo + '" class= "VAS-TaxDetail mb-2" > ' +
                '</div>' +
                '</div>');
        };
        /*This function is used to get Line History  data*/
        this.getLineHistoryPanel = function (recordID) {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/PoReceipt/GetLineHistoryData",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { OrderLineID: recordID },
                success: function (data) {
                    if (JSON.parse(data) != "") {
                        data = JSON.parse(data);
                        console.log(data);
                        if (data != null && data.length > 0) {
                            for (i = 0; i < data.length; i++) {
                                var TabPaneldesign = '<div class="vas-apListItem mb-2">'+
                                    '<div class="vas-ap-sgl1Item1_First mb-2">'+
                                   '<div class="vas-singleTaxElement ">' +
                                   '<span class="vas-singleTaxElementTTl font-weight-bold">'+ VAS.translatedTexts.VAS_LineNo + '</span>'+
                                   '<span class="vas-singleTaxElementValue1_first">' + data[i].LineNo + '</span>'+
                                   '</div>'+
                                 '<div class="vas-singleTaxElement ">' +
                                 '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_DateOrdered + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + new Date(data[i].DateOrdered).toLocaleDateString() + '</span>'+
                                 '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_DatePromised + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + new Date(data[i].DatePromised).toLocaleDateString()+ '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                               '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Product + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].Product + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Charge + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].Charge + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Quantity + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].Quantity + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_UOM + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].UOM + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_QuantityOrdered + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].QuantityOrdered + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Price + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].Price + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_ListPrice + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].ListPrice + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_UnitPrice + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].UnitPrice + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Tax + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].Tax + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                               '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Discount + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].Discount + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_LineAmount + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].LineAmount + '</span>'+
                                '</div>'+
                                '<div class="vas-singleTaxElement ">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Description + '</span>'+
                                '<span class="vas-singleTaxElementValue1_first">' + data[i].Description + '</span>'+
                                '</div>'+
                                '</div>'
                                '</div>';

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

        VAS.LineHistoryTabPanel.prototype.startPanel = function (windowNo, curTab) {
            this.windowNo = windowNo;
            this.curTab = curTab;
            this.init();
        };
        /* This function to update tab panel based on selected record */
        VAS.LineHistoryTabPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getLineHistoryPanel(recordID);
        };
        /* This will set width as per window width in dafault case it is 75% */
        VAS.LineHistoryTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };
        /* Disposing all variables from memory */
        VAS.LineHistoryTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
            lblTitle = null;
        }
    }
})(VAS, jQuery);