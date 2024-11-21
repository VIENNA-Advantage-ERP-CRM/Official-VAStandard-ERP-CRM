/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get Invoice schedule Data
 * chronological  : Development
 * Created Date   : 22 October 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {


    VAS.VAS_ScheduleDetail = function () {

        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var ctx = this.ctx;
        var pageNo = 1;
        var pageSize = 10;
        var $self = this;
        var TotalPageCount = 0;
        var ScheduleDataArray = [];
        var C_Invoice_ID = 0;
        var elements = [
            "VAS_LineNum",
            "DueDate",
            "DueAmt",
            "VA009_PaymentMethod_ID",
            "IsPaid",
            "DateAcct",
            "VAS_Payment",
            "DocumentNo",
            "C_BankAccount_ID",
            "VAS_CheckDate",
            "VAS_CheckNo",
            "VAS_ClickToSeePayment",
            "VAS_NotPaid"
        ];
        var paymentData = [];
        var ulPaging = null;
        var liFirstPage = null;
        var liPrevPage = null;
        var cmbPage = null;
        var liCurrPage = null;
        var liNextPage = null;
        var liLastPage = null;
        var divPaging = null;
        var TabPaneldesign = null;
        var IsToAppend = false;
        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);
        var $root = $('<div class = "vas-scheduleData-root"></div>');
        var wrapperDiv = $("<div class ='vas-scheduleDataWrapper'></div>");
        var divPaging = $('<div class="VAS-scheduledata-Paging mr-3">');

        this.init = function () {
            ScheduleDataPanel();
            $root.append(wrapperDiv);
            createPageSettings();
            $root.append(divPaging);
        };
        //Function defined to append design
        function ScheduleDataPanel() {
            wrapperDiv.append(
                '<div class="vas-scheduleDataListGroup">' +
                '<div id="VAS-ScheduleData_' + $self.windowNo + '" class="VAS-ScheduleData mb-2">' +
                '</div>' +
                '</div>'
            );
        };
        /**
         *  function used to reset the paging according to the 
         *  current page count 
         * @param {any} TotalPageCount
         */
        function resetPageCtrls(TotalPageCount) {
            cmbPage.empty();
            if (TotalPageCount > 0) {
                for (var i = 0; i < TotalPageCount; i++) {
                    cmbPage.append($("<option value=" + (i + 1) + ">" + (i + 1) + "</option>"))
                }
                cmbPage.val(pageNo);


                if (TotalPageCount > pageNo) {
                    liNextPage.css("opacity", "1");
                    liLastPage.css("opacity", "1");
                }
                else {
                    liNextPage.css("opacity", "0.6");
                    liLastPage.css("opacity", "0.6");
                }

                if (pageNo > 1) {
                    liFirstPage.css("opacity", "1");
                    liPrevPage.css("opacity", "1");
                }
                else {
                    liFirstPage.css("opacity", "0.6");
                    liPrevPage.css("opacity", "0.6");
                }

                if (TotalPageCount == 1) {
                    liFirstPage.css("opacity", "0.6");
                    liPrevPage.css("opacity", "0.6");
                    liNextPage.css("opacity", "0.6");
                    liLastPage.css("opacity", "0.6");
                }
            }
            else {
                liFirstPage.css("opacity", "0.6");
                liPrevPage.css("opacity", "0.6");
                liNextPage.css("opacity", "0.6");
                liLastPage.css("opacity", "0.6");
            }
        };
        /*function is used to create the paging div*/
        function createPageSettings() {
            ulPaging = $('<ul class="vis-statusbar-ul">');
            liFirstPage = $('<li style="opacity: 1;"><div><i class="vis vis-shiftleft" title="First Page" style="opacity: 0.6;"></i></div></li>');
            liPrevPage = $('<li style="opacity: 1;"><div><i class="vis vis-pageup" title="Page Up" style="opacity: 0.6;"></i></div></li>');
            cmbPage = $('<select>');
            liCurrPage = $('<li>').append(cmbPage);
            liNextPage = $('<li style="opacity: 1;"><div><i class="vis vis-pagedown" title="Page Down" style="opacity: 0.6;"></i></div></li>');
            liLastPage = $('<li style="opacity: 1;"><div><i class="vis vis-shiftright" title="Last Page" style="opacity: 0.6;"></i></div></li>');
            ulPaging.append(liFirstPage).append(liPrevPage).append(liCurrPage).append(liNextPage).append(liLastPage);
            divPaging.append(ulPaging);
            pageEvents();
        };
        /* function used to create click events for the paging arrows */
        function pageEvents() {
            liFirstPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo = 1;
                    $self.getInvoiceTaxData(C_Invoice_ID);
                }
            });
            liPrevPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo--;
                    $self.getInvoiceTaxData(C_Invoice_ID);
                }
            });
            liNextPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo++;
                    $self.getInvoiceTaxData(C_Invoice_ID);
                }
            });
            liLastPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo = parseInt(cmbPage.find("Option:last").val());
                    $self.getInvoiceTaxData(C_Invoice_ID);
                }
            });
            cmbPage.on("change", function () {
                pageNo = cmbPage.val();
                $self.getInvoiceTaxData(C_Invoice_ID);
            });
        };
        /*This function is used to get invoice schedule data*/
        this.getInvoiceTaxData = function (recordID) {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/PoReceipt/GetScheduleData",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { InvoiceId: recordID, pageNo: pageNo, pageSize: pageSize },
                success: function (data) {
                    wrapperDiv.find('#VAS-ScheduleData_' + $self.windowNo).empty();
                    if (JSON.parse(data) != "") {
                        data = JSON.parse(data);
                        if (data != null && data.length > 0) {
                            ScheduleDataArray = data;
                            TotalPageCount = Math.ceil(data[0].RecordCount / pageSize);
                            for (i = 0; i < data.length; i++) {

                                TabPaneldesign = '<div data-payscheduleid="' + data[i].C_InvoicePaySchedule_ID + '" class="vas-scheduledatalist">' +
                                    '<div class="vas-scheduleDataListItem mb-2 vas-scheduleDataListItem-block" title="' + VAS.translatedTexts.VAS_ClickToSeePayment + '">';
                                if (data[i].IsPaid == "Y") {
                                    TabPaneldesign += '<span class="vas-scheduleDataListPaid">' + VAS.translatedTexts.IsPaid + '</span>';
                                }
                                else {
                                    TabPaneldesign += '<span class="vas-scheduleDataListNotPaid">' + VAS.translatedTexts.VAS_NotPaid + '</span>';
                                }
                                TabPaneldesign +=
                                    '<div class="vas-scheduleData-sglItem mb-2">' +
                                    '<div class="vas-scheduleDataElement">' +
                                    '<span class="vas-scheduleDataElementTTl font-weight-bold">' + VAS.translatedTexts.DueDate + '</span>' +
                                    '<span class="vas-scheduleDataElementValue">' + VIS.Utility.Util.getValueOfDate(data[i].DueDate).toLocaleDateString() + '</span>' +
                                    '</div>' +
                                    '<div class="vas-scheduleDataElement vas-setTaxPaybleAmtWidth">' +
                                    '<span class="vas-scheduleDataElementTTl font-weight-bold text-right">' + VAS.translatedTexts.DueAmt + '</span>' +
                                    '<span class="vas-scheduleDataElementValue text-right">' + (data[i].DueAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].stdPrecision, maximumFractionDigits: data[i].stdPrecision }) + '</span>' +
                                    '</div>' +
                                    '<div class="vas-scheduleDataElement vas-setTaxAmtWidth">' +
                                    '<span class="vas-scheduleDataElementTTl font-weight-bold" title="' + VAS.translatedTexts.VA009_PaymentMethod_ID + '">' + VAS.translatedTexts.VA009_PaymentMethod_ID + '</span>' +
                                    '<span class="vas-scheduleDataElementValue">' + data[i].PayMethod + '</span>' +
                                    '</div>' +
                                    '</div >' +
                                    '</div>' +
                                    '<div id="VAS-PaymentData_' + $self.windowNo + '" class="VAS-PaymentData mb-2">' +
                                    '</div>' +
                                    '</div>';
                                TabPaneldesign += '<div id="VAS-PaymentData_' + $self.windowNo + '" class="VAS-PaymentData mb-2">' +
                                    '</div>' +
                                    '</div>';

                                //Appending design to wrapperDiv
                                wrapperDiv.find('#VAS-ScheduleData_' + $self.windowNo).append(TabPaneldesign);
                            }
                            $root.find('.VAS-scheduledata-Paging').show();
                            resetPageCtrls(TotalPageCount);

                            /* handled click event of schedule div to get payment associated with it*/
                            wrapperDiv.find('#VAS-ScheduleData_' + $self.windowNo).find('.vas-scheduledatalist').on("click", function () {

                                var scheduleId = $(this).attr("data-payscheduleid");
                                paymentData = jQuery.grep(ScheduleDataArray, function (value) {
                                    return VIS.Utility.Util.getValueOfInt(value.C_InvoicePaySchedule_ID) == VIS.Utility.Util.getValueOfInt(scheduleId)
                                });
                                $(this).find('.VAS-PaymentData').click(function (e) {
                                    e.stopPropagation();
                                });
                                wrapperDiv.find('#VAS-ScheduleData_' + $self.windowNo).find('.vas-scheduleDataListItem').removeClass('vas-scheduleData-Active');
                                $(this).find('.vas-scheduleDataListItem').addClass('vas-scheduleData-Active');
                                TabPaneldesign =
                                    '<div class="vas-paymentDataListItem vas-newListItem mb-2">' +
                                    '<div class="vas-paymentheading">' + VAS.translatedTexts.VAS_Payment + '</div>' +
                                    '<hr class="vas-paymentData-underline">' +
                                    '<div class="vas-paymentData-sglItem mb-2">' +
                                    '<div class="vas-paymentDataElement">' +
                                    '<span class="vas-paymentDataElementTTl font-weight-bold">' + VAS.translatedTexts.DocumentNo + '</span>' +
                                    '<span class="vas-paymentDataElementValue">' + (paymentData[0].DocumentNo || '') + '</span>' +
                                    '</div>' +
                                    '<div class="vas-paymentDataElement">' +
                                    '<span class="vas-paymentDataElementTTl font-weight-bold">' + VAS.translatedTexts.DateAcct + '</span>' +
                                    '<span class="vas-paymentDataElementValue">' + (paymentData[0].DateAcct != null ? VIS.Utility.Util.getValueOfDate(paymentData[0].DateAcct).toLocaleDateString() : '') + '</span>' +
                                    '</div>' +
                                    '<div class="vas-paymentDataElement">' +
                                    '<span class="vas-paymentDataElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_CheckDate + '</span>' +
                                    '<span class="vas-paymentDataElementValue">' + (paymentData[0].CheckDate != null ? VIS.Utility.Util.getValueOfDate(paymentData[0].CheckDate).toLocaleDateString() : '') + '</span>' +
                                    '</div>' +
                                    '<div class="vas-paymentDataElement">' +
                                    '<span class="vas-paymentDataElementTTl font-weight-bold">' + VAS.translatedTexts.VAS_CheckNo + '</span>' +
                                    '<span class="vas-paymentDataElementValue">' + (paymentData[0].CheckNo || '') + '</span>' +
                                    '</div>' +
                                    '</div>' +
                                    '</div>' +
                                    '</div>';
                                /*Handled the appending of payment div*/
                                if ($(this).find('#VAS-PaymentData_' + $self.windowNo).find('.vas-paymentDataListItem').length > 0) {
                                    IsToAppend = false;
                                }
                                else {
                                    IsToAppend = true;
                                    wrapperDiv.find('#VAS-ScheduleData_' + $self.windowNo).find('.vas-scheduledatalist').find('#VAS-PaymentData_' + $self.windowNo).empty()
                                }
                                if (IsToAppend && paymentData[0].IsPaid == "Y") {
                                    $(this).find('#VAS-PaymentData_' + $self.windowNo).append(TabPaneldesign);
                                }
                                else if (paymentData[0].IsPaid == "Y") {
                                    $(this).find('.vas-scheduleDataListItem').removeClass('vas-scheduleData-Active');
                                    $(this).find('#VAS-PaymentData_' + $self.windowNo).empty();
                                }
                            })

                        }

                    }
                    else {
                        $root.find('.VAS-scheduledata-Paging').hide();
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

        VAS.VAS_ScheduleDetail.prototype.startPanel = function (windowNo, curTab) {
            this.windowNo = windowNo;
            this.curTab = curTab;
            $self.windowNo = windowNo;
            this.init();
        };
        /* This function to update tab panel based on selected record */
        VAS.VAS_ScheduleDetail.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            C_Invoice_ID = recordID;
            pageNo = 1;
            pageSize = 10;
            this.selectedRow = selectedRow;
            this.getInvoiceTaxData(recordID);
        };
        /* This will set width as per window width in dafault case it is 75% */
        VAS.VAS_ScheduleDetail.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };
        /* Disposing all variables from memory */
        VAS.VAS_ScheduleDetail.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
            lblTitle = null;
        }
    }
})(VAS, jQuery);