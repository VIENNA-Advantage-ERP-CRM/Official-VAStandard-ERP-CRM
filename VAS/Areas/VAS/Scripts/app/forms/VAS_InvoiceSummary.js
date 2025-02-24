/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get Invoice Summary Data
 * chronological  : Development
 * Created Date   : 16 September 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {
    VAS.VAS_InvoiceSummary = function () {

        this.windowNo;
        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var ctx = this.ctx;
        var self = this;

        var $root = $('<div class="root"></div>');
        var wrapperDiv = null;

        this.init = function () {
            wrapperDiv = $('<div class ="vas-apTaxWrapper">' +
                '<div id="VAS-TaxDetail_' + self.windowNo + '" class="VAS-summary-details mb-2">' +
                '</div></div>');
            $root.append(wrapperDiv);
        };

        /*This function is used to get Order tax data*/
        this.getInvoiceSummary = function (recordID) {
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetTaxData",
                { "InvoiceID": recordID }, function (data) {
                    wrapperDiv.find('#VAS-TaxDetail_' + self.windowNo).empty();
                    if (data != null && data.length > 0) {
                        wrapperDiv.find('#VAS-TaxDetail_' + self.windowNo).append($('<div class="VAS-summary-header">' +
                            '<span class="">' + VIS.Msg.getMsg("VAS_SubTotal") + ":" + '</span>' +
                            '</div>'));
                        wrapperDiv.find('#VAS-TaxDetail_' + self.windowNo).append($('<div class="VAS-summary-info">' +
                            '<span class="VAS-summary-amount" style="font-weight: 600;">' + data[0].CurSymbol + " "
                            + (data[0].TotalLines).toLocaleString(window.navigator.language, { minimumFractionDigits: data[0].stdPrecision, maximumFractionDigits: data[0].stdPrecision }) + '</span>' +
                            '</div>'));

                        for (i = 0; i < data.length; i++) {
                            wrapperDiv.find(".VAS-summary-header").append('<span class="">' + VIS.Utility.encodeText(data[i].TaxName) + ":" + '</span>');
                            wrapperDiv.find(".VAS-summary-info").append('<span class="VAS-summary-amount">' + data[0].CurSymbol + " "
                                + (data[i].TaxAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: data[0].stdPrecision, maximumFractionDigits: data[0].stdPrecision }) + '</span>');
                        }

                        wrapperDiv.find(".VAS-summary-header").append('<span class="">' + VIS.Msg.getMsg("GrandTotal") + ":" + '</span>');
                        wrapperDiv.find(".VAS-summary-info").append('<span class="VAS-summary-amount" style="font-weight: 600;font-size: 1rem;border-top: 1px solid rgba(var(--v-c-on-secondary), 1);">' + data[0].CurSymbol + " "
                            + (data[0].GrandTotal).toLocaleString(window.navigator.language, { minimumFractionDigits: data[0].stdPrecision, maximumFractionDigits: data[0].stdPrecision }) + '</span>');
                    }
                });
        }

        this.getRoot = function () {
            return $root;
        };

        VAS.VAS_InvoiceSummary.prototype.startPanel = function (windowNo, curTab) {
            self.windowNo = windowNo;
            self.curTab = curTab;
            self.init();
        };

        /* This function to update tab panel based on selected record */
        VAS.VAS_InvoiceSummary.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getInvoiceSummary(recordID);
        };

        /* This will set width as per window width in dafault case it is 75% */
        VAS.VAS_InvoiceSummary.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };

        /* Disposing all variables from memory */
        VAS.VAS_InvoiceSummary.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
        }
    }
})(VAS, jQuery);