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
            "VAS_QuantityReq",
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
                    wrapperDiv.find('#VAS-TaxDetail_' + self.windowNo).empty();
                    data = JSON.parse(data);
                    if (data != null && data.length > 0) {
                        for (i = 0; i < data.length; i++) {
                            //VIS430:Handled Duplication of data on Requisition Tab Panel
                            var TabPaneldesign = '<div class="vas-apListItem mb-2">' +
                                '<div class="vas-ap-sglItem d-flex flex-column mb-2">' +
                                '<div class="vas-singleItemListWrap mb-2">' +
                                '<div class="vas-singleTaxElement vas-setTaxWidth vas-setUomWidth">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold w-100">' + VAS.translatedTexts.VAS_LineNo + '</span>' +
                                '<span class="vas-singleRequesitionValue">' + data[i].LineNo + '</span>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setTaxPaybleAmtWidth vas-setUomWidth">' +
                                '<span class="vas-singleTaxElementTTl font-weight-bold w-100">' + VAS.translatedTexts.VAS_Requition + '</span>' +
                                '<span class="vas-singleRequesitionValue w-100">' + data[i].RequitionDocumentNo + '</span>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setShipWidth vas-setChargeWidth">' +
                                '<span class="vas-singleTaxElementTTl w-100 font-weight-bold vas-tp-widthpro">' + VAS.translatedTexts.VAS_Product + '</span>' +
                                '<span class="vas-singleRequesitionValue w-100">' + data[i].ProductName + '</span>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setTaxProWidth vas-setUomWidth">' +
                                '<span class="vas-singleTaxElementTTl w-100 font-weight-bold">' + VAS.translatedTexts.VAS_QuantityReq + '</span>' +
                                '<span class="vas-singleRequesitionValue w-100">' + data[i].Qty.toLocaleString() + '</span>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setTaxProWidth vas-setChargeWidth">' +
                                '<span class="vas-singleTaxElementTTl w-100 font-weight-bold vas-setShipWidth">' + VAS.translatedTexts.VAS_Charge + '</span>' +
                                '<span class="vas-singleRequesitionValue w-100">' + data[i].ChargeName + '</span>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setTaxProWidth vas-setUomWidth">' +
                                '<span class="vas-singleTaxElementTTl w-100 font-weight-bold">' + VAS.translatedTexts.VAS_Uom + '</span>' +
                                '<span class="vas-singleRequesitionValue w-100">' + data[i].UomName + '</span>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setTaxProWidth vas-setUomWidth">' +
                                '<span class="vas-singleTaxElementTTl w-100 font-weight-bold">' + VAS.translatedTexts.VAS_UnitPrice + '</span>' +
                                '<span class="vas-singleRequesitionValue w-100">' + (data[i].PriceActual).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].StdPrecision, maximumFractionDigits: data[i].StdPrecision }) + '</span>' +//VIS430:Handled Precision unit price on Requisition Tab Panel
                                '</div>' +
                                '<div class="vas-singleTaxElement vas-setTaxProWidth vas-setUomWidth">' +
                                '<span class="vas-singleTaxElementTTl w-100 font-weight-bold">' + VAS.translatedTexts.VAS_LineAmount + '</span>' +
                                '<span class="vas-singleRequesitionValue w-100">' + (data[i].LineNetAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].StdPrecision, maximumFractionDigits: data[i].StdPrecision }) + '</span>' + //VIS430:Handled Precision Line Amount on Requisition Tab Panel
                                '</div>' +
                                '</div>' +
                                '<div class="vas-singleTaxElement">' +
                                '<span class="vas-singleTaxElementTTl w-100 font-weight-bold vas-setReqAmtWidth">' + VAS.translatedTexts.VAS_Description + '</span>' +
                                '<span class="vas-singleTaxDescription">' + data[i].Description + '</span>' +
                                '</div>' +
                                '</div >' +
                                '</div>'
                            //Appending design to wrapperDiv
                            wrapperDiv.find('#VAS-TaxDetail_' + this.windowNo).append(TabPaneldesign);
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