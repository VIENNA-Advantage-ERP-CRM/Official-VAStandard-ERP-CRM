/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to view the Invoice Report
 * chronological  : Development
 * Created Date   : 26 August 2024
 * Created by     : VIS-383
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {
    VAS.InvoiceLineTabPanel = function () {

        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
       // var $bsyDiv = null;
        var ctx = this.ctx;
        var windowNo = 0;
        var windowID = 0;
        var $iFrameDiv = null;
        var $root = $('<div class = "root h-100"></div>');
        var wrapperDiv = $("<div class ='vas-apInvWrapper h-100'></div>");
        var $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div></div>');
       
        

        this.init = function () {
            windowNo = this.windowNo;
            InvoiceLinePanel(windowNo);
            $root.append(wrapperDiv).append($bsyDiv);
        };
        /*Function defined to append design*/
        function InvoiceLinePanel(windowNo) {
            wrapperDiv.append(
                '<div class="vas-apInvListGroup h-100">' +
                '<div id="VAS-InvDetail_' + windowNo + '" class= "VAS-InvDetail mb-2 h-100">'+ 
                '</div>' +
                '</div>');
            $iFrameDiv = $('<iframe id="iframeinv' + windowNo + '" src="" width="100%" height="100%" frameborder="0" scrolling="no">');
            wrapperDiv.find('#VAS-InvDetail_' + windowNo).append($iFrameDiv);
        };

        /*VIS-383: 27/07/24:-This function is used to get invoice data for pdf*/
        this.getInvoiceLineData = function (recordID) {
            busyDiv(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/PoReceipt/GetInvoiceLineReport",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { InvoiceLineId: recordID, AD_WindowID: windowID },
                success: function (data) {
                    //var urlPath = null;
                    /*Clear iFrame when select new invoice*/
                    document.getElementById('iframeinv' + windowNo).src = '';
                    if (data != "") {
                        var data = JSON.parse(data);
                        console.log(data);
                        if (data != "" && data.length > 0) {
                            var urlPath = VIS.Application.contextFullUrl + "" + data;
                            /*Create iFrame to view invoice report using report path*/
                            document.getElementById('iframeinv'+ windowNo).src = urlPath;
                            busyDiv(false);
                        }
                    }
                    busyDiv(false);
                },
                error: function (eror) {
                    console.log(eror);
                    busyDiv(false);
                }
            })
        }

        function busyDiv(Value) {
            if (Value) {
                $bsyDiv[0].style.visibility = 'visible';
            }
            else {
                $bsyDiv[0].style.visibility = 'hidden';
            }
        };
        /*Retrun container of panel's Design*/
        this.getRoot = function () {
            return $root;
        };
        /*Invoked when user click on panel icon*/
        VAS.InvoiceLineTabPanel.prototype.startPanel = function (windowNo, curTab) {
            this.windowNo = windowNo;
            this.curTab = curTab;
            windowID = this.curTab.getAD_Window_ID();
            this.init();
        };
        /* This function will execute when user navigate or refresh a record */
        VAS.InvoiceLineTabPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.windowNo = windowNo;
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getInvoiceLineData(recordID);
        };
        /* Set width as per window width in dafault case it is 75% */
        VAS.InvoiceLineTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };
        /* Disposing all variables from memory */
        VAS.InvoiceLineTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.panelWidth = null;
            windowNo = 0;
            windowID = 0;
        }
    }
})(VAS, jQuery);