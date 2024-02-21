/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get Requition Lines Data
 * chronological  : Development
 * Created Date   : 19 February 2024
 * Created by     : VIS430
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {


    VAS.RequisitionLinesTabPanel = function () {

        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var lblTitle = null;
        var ctx = this.ctx;

        var elements = [
            "VAS_ReuitionLinesTabPanel",
            "VAS_LineNo",
            "VAS_Requition",
            "VAS_Product",
            "VAS_Charge",
            "VAS_Quantity",
            "VAS_Uom",
            "VAS_UnitPrice",
            "VAS_LineAmount",
            "VAS_Description",
        ];

        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);

        var $root = $('<div class = "root"></div>');
        var wrapperDiv = $("<div class ='vas-apTaxWrapper'></div>")
        lblTitle = $("<h6 class='vas-apTax-ttl'>" + VAS.translatedTexts.VAS_ReuitionLinesTabPanel + "</h6>");
        this.init = function () {
            wrapperDiv.append(lblTitle);
            ReuitionLinesPanel();
            $root.append(wrapperDiv);
        };
        //Function defined to append design
        function ReuitionLinesPanel() {
            wrapperDiv.append(
                '<div class="vas-aptaxListGroup">' +
                '<div id="VAS-TaxDetail_' + this.windowNo + '" class= "VAS-TaxDetail mb-2" > ' +
                '</div>' +
                '</div>');
        };
        /*This function is used to get Requisition data*/
        this.getReuitionLinesData = function (recordID) {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/OrderLineTabPanel/GetRequitionLinesData",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { OrderLineId: recordID },
                success: function (data) {
                    if (JSON.parse(data) != "") {
                        data = JSON.parse(data);
                        if (data != null && data.length > 0) {
                            for (i = 0; i < data.length; i++) {
                                var TabPaneldesign = '<div class="vas-apListItem mb-2">' +
                                    '<div class="vas-ap-sglItem mb-2">' +
                                    '<div class="vas-singleTaxElement vas-setTaxWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_LineNo + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].LineNo + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxPaybleAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Requition + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].RequisitionId +  '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Product + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].ProductId + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_QuantityReq + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].Qty + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Charge + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].ChargeId + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Uom + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].UomId + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_UnitPrice + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].PriceActual + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_LineAmount + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].LineNetAmt + '</span>' +
                                    '</div>' +
                                    '<div class="vas-singleTaxElement vas-setTaxAmtWidth">' +
                                    '<span class="vas-singleTaxElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_Description + '</span>' +
                                    '<span class="vas-singleTaxElementValue">' + data[i].Description + '</span>' +
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

        VAS.RequisitionLinesTabPanel.prototype.startPanel = function (windowNo, curTab) {
            this.windowNo = windowNo;
            this.curTab = curTab;
            this.init();
        };
        /* This function to update tab panel based on selected record */
        VAS.RequisitionLinesTabPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getReuitionLinesData(recordID);
        };
        /* This will set width as per window width in dafault case it is 75% */
        VAS.RequisitionLinesTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };
        /* Disposing all variables from memory */
        VAS.RequisitionLinesTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
            lblTitle = null;
        }
    }
})(VAS, jQuery);