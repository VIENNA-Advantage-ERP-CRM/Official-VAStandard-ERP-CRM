/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get UnAllocated Payments
 * chronological  : Development
 * Created Date   : 08 August 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {


    VAS.UnAllocatedPaymentTabPanel = function () {

        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var $self = this;
        var ctx = this.ctx;
        /*Stored messages which is to be translated*/
        var elements = [
            "DateTrx",
            "DateAcct",
            "VAS_PayNo",
            "VAS_Amount",
            "C_Payment_ID",
            "AD_Org_ID",
            "VAS_UnaAllocatedPayTabPanel",
            "VAS_Zoom",
            "AD_Window_ID",
            "C_ConversionType_ID",
            "VAS_RecordNo"
        ];

        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);

        var $root = $('<div class="VAS-unallocated-flyout" style="width: 100%;">');
        var wrapperDiv = $('<div class="VAS-payment-details">');
        /*this is init fucntion to create design*/
        this.init = function () {
            var UnAllocatedPaymentList = $('<ul id="VAS-unallocated_' + $self.windowNo + '"></ul></div>');
            wrapperDiv.append(UnAllocatedPaymentList);
            $root.append(wrapperDiv);
        };
        /**
       * this fucntion is used to zoom to payment window for the record which is clicked from grid
       * @param {any} PaymentId
       * @param {any} windowId
       */
        function handleZoomClick(PaymentId, windowId) {
            if (windowId > 0) {
                var zoomQuery = new VIS.Query();
                zoomQuery.addRestriction("C_Payment_ID", VIS.Query.prototype.EQUAL, PaymentId);
                zoomQuery.setRecordCount(1);
                VIS.viewManager.startWindow(windowId, zoomQuery);
            }
        }
        /**
       * this fucntion is used to get the unallocated payment for particular bussiness partner
       * @param {any} C_BPartner_ID
       * @param {any} IsSoTrx
       * @param {any} AD_Org_ID
       */
        this.getUnAllocatedPayData = function (C_BPartner_ID, IsSoTrx, AD_Org_ID) {            
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/PoReceipt/GetUnAllocatedPayData",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { C_BPartner_ID: C_BPartner_ID, IsSoTrx: IsSoTrx, AD_Org_ID: AD_Org_ID },
                success: function (res) {
                    wrapperDiv.find('#VAS-unallocated_' + $self.windowNo).empty();
                    var gridDataResult = JSON.parse(res);
                    for (var i = 0; i < gridDataResult.length; i++) {
                        var TabPaneldesign = '<li>' +
                            '<span class="VAS-search-link">' +
                            '<i class="glyphicon glyphicon-zoom-in" data-PaymentId="' + gridDataResult[i].C_Payment_ID + '" data-windowId="' + gridDataResult[i].AD_Window_ID + '" id="VAS-unAllocatedZoom_' + $self.windowNo + '"></i>' +
                            '</span>' +
                            //'<div class="form-check">' +
                            //'<input class="form-check-input position-static" type="checkbox" id="blankCheckbox" value="option1" aria-label="..."> ' +
                            //'</div>' +
                            '<div class="VAS-payment-opt w-100">' +
                            '<div class="VAS-paymentDetail">' +
                            '<div class="VAS-payment-val">' +
                            '<label for="" title="' + VAS.translatedTexts.VAS_PayNo +'">' + VAS.translatedTexts.VAS_PayNo + '</label>' +
                            '<span class="vas-ualdocno" title="' + gridDataResult[i].DocumentNo +'">' + gridDataResult[i].DocumentNo+'</span>' +
                            '</div>' +
                            '<div class="VAS-payment-val">' +
                            '<label for="" title="' + VAS.translatedTexts.DateAcct +'">' + VAS.translatedTexts.DateAcct + '</label>' +
                            '<span>' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].DateAcct).toLocaleDateString() +'</span>' +
                            '</div>' +
                            //'<div class="VAS-payment-val">' +
                            //'<label for="" title="' + VAS.translatedTexts.DateTrx +'">' + VAS.translatedTexts.DateTrx + '</label>' +
                            //'<span>' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].DateTrx).toLocaleDateString() +'</span>' +
                            //'</div>' +
                            '<div class="VAS-paymentDetail VAS-grid-col1">' +
                            '<div class="VAS-payment-val">' +
                            '<label for="" title="' + VAS.translatedTexts.VAS_Amount + '">' + VAS.translatedTexts.VAS_Amount + '</label>' +
                            '<span class="VAS-totalPrice">' + (gridDataResult[i].PayAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: gridDataResult[i].StdPrecision, maximumFractionDigits: gridDataResult[i].StdPrecision }) + ' ' + gridDataResult[i].CurrencyName+'</span>' +
                            '</div>' +
                            '</div>' +
                            '</div>' +
                            '</div>' +
                            '</li>';
                        //Appending design to wrapperDiv
                        wrapperDiv.find('#VAS-unallocated_' + $self.windowNo).append(TabPaneldesign);
                    }
                    //Applied click event fro zoom window
                    wrapperDiv.find('.glyphicon-zoom-in').on("click", function () {
                        var PaymentId = VIS.Utility.Util.getValueOfInt($(this).attr("data-PaymentId"));
                        var windowId = VIS.Utility.Util.getValueOfInt($(this).attr("data-windowId"));
                        handleZoomClick(PaymentId, windowId);
                    })
                },
                error: function (eror) {
                    console.log(eror);
                }
            })
        }
        /*this fucntion is used to get the root for design*/
        this.getRoot = function () {
            return $root;
        };
        

        VAS.UnAllocatedPaymentTabPanel.prototype.startPanel = function (windowNo, curTab) {
            this.windowNo = windowNo;
            this.curTab = curTab;
            $self.windowNo = windowNo;
            this.init();
        };
        /* This function to update tab panel based on selected record */
        VAS.UnAllocatedPaymentTabPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getUnAllocatedPayData(selectedRow.c_bpartner_id, selectedRow.issotrx, selectedRow.ad_org_id);
        };
        /* This will set width as per window width in dafault case it is 75% */
        VAS.UnAllocatedPaymentTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };
        /* Disposing all variables from memory */
        VAS.UnAllocatedPaymentTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.curTab = null;
            this.selectedRow = null;
            this.panelWidth;
            lblTitle = null;
            ctx = this.ctx;
        }
    }
})(VAS, jQuery);