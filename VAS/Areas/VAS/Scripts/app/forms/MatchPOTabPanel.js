/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get Match PO Data of Purchase order Window
 * chronological  : Development
 * Created Date   : 20 February 2024
 * Created by     : VIS430
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {


    VAS.MatchPOTabPanel = function () {

        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var lblTitle = null;
        var ctx = this.ctx;

        var elements = [
            "VAS_MatchPoTabPanel",
            "VAS_TransactionDate",
            "VAS_PurchaseOrderNo",
            "VAS_OrderLine",
            "VAS_ShipmentLine",
            "VAS_InvoiceLineMatch",
            "VAS_Product",
            "VAS_AttributeSetInstance",
            "VAS_QuantityReq",
        ];

        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);

        var $root = $('<div class = "root"></div>');
        var wrapperDiv = $("<div class ='vas-apTaxWrapper'></div>")
        lblTitle = $("<h6 class='vas-apTax-ttl'>" + VAS.translatedTexts.VAS_MatchPoTabPanel + "</h6>");
        this.init = function () {
            wrapperDiv.append(lblTitle);
            MatchPoPanel();
            $root.append(wrapperDiv);
        };
        //Function defined to append design
        function MatchPoPanel() {
            wrapperDiv.append(
                '<div class="vas-aptaxListGroup">' +
                '<div id="VAS-TaxDetail_' + this.windowNo + '" class= "VAS-TaxDetail mb-2" > ' +
                '</div>' +
                '</div>');
        };
        /*This function is used to get Match PO data*/
        this.getMatchPoData = function (recordID) {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/OrderLineTabPanel/GetMatchingData",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { OrderLineId: recordID },
                success: function (data) {
                    if (JSON.parse(data) != "") {
                        data = JSON.parse(data);
                        if (data != null && data.length > 0) {
                        wrapperDiv.find('#VAS-TaxDetail_' + self.windowNo).empty();
                            for (i = 0; i < data.length; i++) {

                                //VIS430:Handled Duplication of data on Match PO Tab Panel
                               
                                var TabPaneldesign = '<div class="vas-apListItem mb-2">' +
                                    '<div class="vas-ap-sglItem mb-2">' +
                                    '<div class="vas-singleTaxElement vas-setMatchPo">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_TransactionDate + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + new Date(data[i].TransactionDate).toLocaleDateString() + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setMatchPo">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_PurchaseOrderNo + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].MatchPoNo + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setMatchPo">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_OrderLine + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].PoDocumentNo + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setMatchPo">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_ShipmentLine + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].ShipmentLine + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setMatchPo">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_InvoiceLineMatch + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].InvoiceDocNo + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setShipWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Product + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].Product + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setShipWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_AttributeSetInstance + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].AttributeSetInstance + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setMatchPo">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_QuantityReq + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].Quantity.toLocaleString() + '</span>' + //VIS430:Handled Qty format on Match PO Tab Panel
                                    '</div>' +
                                    '</div >' +
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

        VAS.MatchPOTabPanel.prototype.startPanel = function (windowNo, curTab) {
            this.windowNo = windowNo;
            this.curTab = curTab;
            this.init();
        };
        /* This function to update tab panel based on selected record */
        VAS.MatchPOTabPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getMatchPoData(recordID);
        };
        /* This will set width as per window width in dafault case it is 75% */
        VAS.MatchPOTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };
        /* Disposing all variables from memory */
        VAS.MatchPOTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
            lblTitle = null;
        }
    }
})(VAS, jQuery);