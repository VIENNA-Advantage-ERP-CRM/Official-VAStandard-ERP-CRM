/************************************************************
 * Module Name    : VAS
 * Purpose        : Tab Panel created to get Invoice Matched Data
 * chronological  : Development
 * Created Date   : 16 November 2024
 * Created by     : VIS_045
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {


    VAS.VAS_InvoiceMatchedTabPanel = function () {

        this.record_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var ctx = this.ctx;
        var pageNo = 1;
        var pageSize = 10;
        var $self = this;
        var TotalPageCount = 0;
        var C_Invoice_ID = 0;

        var ulPaging = null;
        var liFirstPage = null;
        var liPrevPage = null;
        var cmbPage = null;
        var liCurrPage = null;
        var liNextPage = null;
        var liLastPage = null;
        var divPaging = null;
        var TabPaneldesign = null;

        var elements = [
            "VAS_Quantity",
            "VAS_PO",
            "VAS_GRN",
            "VAS_Matched",
            "VAS_UnMatched"
        ];
        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);
        var $root = $('<div class = "vas-invmatch-Data-root"></div>');
        var wrapperDiv = $("<div class ='vas-invmatch-DataWrapper'></div>");
        var divPaging = $('<div class="vas-invmatch-data-Paging mr-3">');

        this.init = function () {
            createBusyIndicator();
            ScheduleDataPanel();
            $root.append(wrapperDiv);
            createPageSettings();
            $root.append(divPaging);
        };

        //Function defined to append design
        function ScheduleDataPanel() {
            wrapperDiv.append(
                '<div class="vas-invmatch-DataListGroup">' +
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
                    $self.getInvoiceLineData(C_Invoice_ID);
                }
            });
            liPrevPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo--;
                    $self.getInvoiceLineData(C_Invoice_ID);
                }
            });
            liNextPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo++;
                    $self.getInvoiceLineData(C_Invoice_ID);
                }
            });
            liLastPage.on("click", function () {
                if ($(this).css("opacity") == "1") {
                    pageNo = parseInt(cmbPage.find("Option:last").val());
                    $self.getInvoiceLineData(C_Invoice_ID);
                }
            });
            cmbPage.on("change", function () {
                pageNo = cmbPage.val();
                $self.getInvoiceLineData(C_Invoice_ID);
            });
        };

        /*This function is used to get invoice schedule data*/
        this.getInvoiceLineData = function (recordID) {
            $bsyDiv[0].style.visibility = "visible";
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/PoReceipt/GetInvoiceLineMatchData",
                type: "GET",
                dataType: "json",
                contentType: "application/json; charset=utf-8",
                data: { InvoiceId: recordID, pageNo: pageNo, pageSize: pageSize },
                success: function (data) {
                    wrapperDiv.find('#VAS-ScheduleData_' + $self.windowNo).empty();
                    wrapperDiv.find('#vas_norecordcont_' + $self.windowNo).remove();
                    if (JSON.parse(data) != "") {
                        data = JSON.parse(data);
                        if (data != null && data.length > 0) {
                            TotalPageCount = Math.ceil(data[0].RecordCount / pageSize);
                            for (i = 0; i < data.length; i++) {
                                TabPaneldesign = "";
                                let OrderClass = data[i].C_OrderLine_ID === 0 ? "vas-invmatch-DataElementValue vas-invmatch-unmatched" : "vas-invmatch-DataElementValue vas-invmatch-matched";
                                let grnClass = data[i].M_InOutLine_ID === 0 ? "vas-invmatch-DataElementValue vas-invmatch-unmatched" : "vas-invmatch-DataElementValue vas-invmatch-matched";

                                if (i == 0 && (data[i].DiscrepancyCount > 0 || data[i].TotalAdvanceAmt != 0)) {
                                    let Values;
                                    if (data[i].DiscrepancyCount > 0) {
                                        Values = VIS.Msg.getMsg("VAS_DiscrepancyFound") + data[i].DiscrepancyCount + VIS.Msg.getMsg("VAS_DiscrepancyLines");
                                    }
                                    if (data[i].TotalAdvanceAmt != 0) {
                                        if (VIS.Utility.Util.getValueOfString(Values) == "") {
                                            Values = data[i].TotalAdvanceAmt + VIS.Msg.getMsg("VAS_AdvancePayNotPaid");
                                        }
                                        else {
                                            Values += data[i].TotalAdvanceAmt + VIS.Msg.getMsg("VAS_AdvancePayNotPaid");
                                        }
                                    }
                                    TabPaneldesign = ('<div class="vas-invmatch-DiscrepancyFont"><span class="vas-invmatch-discrepancytextcolor">' + Values + '</span></div>');
                                }

                                TabPaneldesign += '<div data-C_Invoiceline_ID="' + data[i].C_Invoiceline_ID + '" class="vas-invmatch-datalist">' +
                                    '<div class="vas-invmatch-DataListItem mb-2 vas-invmatch-DataListItem-block">';

                                let Discrepancy = data[i].IsDiscrepancy;

                                if (Discrepancy) {
                                    TabPaneldesign += '<span class="vas-invmatch-DataListNotPaid">' + VIS.Msg.getMsg("VAS_Discrepancy") + '</span>';
                                }
                                else {
                                    TabPaneldesign += '<span class="vas-invmatch-DataListPaid">' + VIS.Msg.getMsg("VAS_Matched") + '</span>';
                                }
                                TabPaneldesign +=
                                    '<div class="vas-invmatch-Data-sglItem mb-2" style="flex-direction:column">' +
                                    '<div class="vas-invmatch-headerRow">' +
                                '<span class="" title="' + VIS.Msg.getMsg("VAS_LineNo") + '">' + data[i].Line + '</span>' +
                                '<span class="font-weight-bold" style="margin-left:25px" title="' + VIS.Msg.getMsg("VAS_Product") + '">' + data[i].ProductName + '</span>' +
                                    '</div>';
                                if (!Discrepancy) {
                                    TabPaneldesign += '<div class="vas-invmatch-Qty">' +
                                        '<span>' + VIS.Msg.getMsg("VAS_Match-QtyOrder") + (data[i].QtyOrdered).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) +
                                        ';' + VIS.Msg.getMsg("VAS_Match-QtyDeliver") + (data[i].OrderDelivered).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) +
                                        ';' + VIS.Msg.getMsg("VAS_Match-QtyInvoice") + (data[i].QtyInvoiced).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) +
                                        '</span>' +
                                        '</div>';
                                }

                                // need to insert div
                                TabPaneldesign += '</div> ';
                                if (Discrepancy) {

                                    TabPaneldesign += '<div class="vas-invmatch-Data-sglItem vas-invmatch-ExpectedMargin mb-2">' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="vas-invmatch-DataElementValue vas-invmatch-setCol1Width">&nbsp;</span>' +
                                        '</div>' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="vas-vas-invmatch-DataElementTTl font-weight-bold">' + VIS.Msg.getMsg("VAS_Expected") + '</span>' +
                                        '</div>' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="vas-vas-invmatch-DataElementTTl font-weight-bold">' + VIS.Msg.getMsg("VAS_Actual") + '</span>' +
                                        '</div>' +
                                        '</div>';

                                    let dataElementValueClass = "vas-invmatch-DataElementValue text-right";
                                    /*if (data[i].C_OrderLine_ID != 0) {*/
                                        if ((data[i].DocStatus == 'CO' || data[i].DocStatus == 'CL')) {
                                            if (data[i].ExpectedOrder < 0) {
                                                dataElementValueClass = "vas-invmatch-DataElementValue text-right vas-invmatch-notMatched";
                                            }
                                        }
                                        else if ((data[i].ExpectedOrder - data[i].QtyInvoiced) < 0) {
                                            dataElementValueClass = "vas-invmatch-DataElementValue text-right vas-invmatch-notMatched";
                                        }
                                    /*}*/
                                    TabPaneldesign += '<div class="vas-invmatch-Data-sglItem vas-invmatch-DataRow mb-2">' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="vas-invmatch-DataElementValue">' + VIS.Msg.getMsg("VAS_Order") + '</span>' +
                                        '</div>' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="' + dataElementValueClass + '" title="' + VIS.Msg.getMsg("VAS_OrderedQty") + '">' +
                                        (data[i].ExpectedOrder + ((data[i].DocStatus == 'CO' || data[i].DocStatus == 'CL') ? data[i].QtyInvoiced : 0)).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) + '</span>' +
                                        '</div>' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="' + dataElementValueClass + '" title="' + VIS.Msg.getMsg("VAS_InvoicedQty") + '">' + (data[i].QtyInvoiced).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) + '</span>' +
                                        '</div>' +
                                        '</div>';

                                    dataElementValueClass = "vas-invmatch-DataElementValue text-right";
                                    if ((data[i].DocStatus == 'CO' || data[i].DocStatus == 'CL')) {
                                        if (data[i].ExpectedGRN < 0) {
                                            dataElementValueClass = "vas-invmatch-DataElementValue text-right vas-invmatch-notMatched";
                                        }
                                    }
                                    else if ((data[i].ExpectedGRN - data[i].QtyInvoiced) < 0) {
                                        dataElementValueClass = "vas-invmatch-DataElementValue text-right vas-invmatch-notMatched";
                                    }
                                    TabPaneldesign += '<div class="vas-invmatch-Data-sglItem vas-invmatch-DataRow mb-2">' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="vas-invmatch-DataElementValue">' + VIS.Msg.getMsg("VAS_GRN") + '</span>' +
                                        '</div>' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="' + dataElementValueClass + '" title="' + VIS.Msg.getMsg("VAS_MovementQty") + '">' +
                                        (data[i].ExpectedGRN + ((data[i].DocStatus == 'CO' || data[i].DocStatus == 'CL') ? data[i].QtyInvoiced : 0)).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) + '</span>' +
                                        '</div>' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="' + dataElementValueClass + '" title="' + VIS.Msg.getMsg("VAS_InvoicedQty") + '">' + (data[i].QtyInvoiced).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) + '</span>' +
                                        '</div>' +
                                        '</div>';

                                    dataElementValueClass = "vas-invmatch-DataElementValue text-right";
                                    if (data[i].C_OrderLine_ID != 0) {
                                        if ((data[i].OrderPrice - data[i].InvoicePrice) < 0) {
                                            dataElementValueClass = "vas-invmatch-DataElementValue text-right vas-invmatch-notMatched";
                                        }
                                    }
                                    TabPaneldesign += '<div class="vas-invmatch-Data-sglItem vas-invmatch-DataRow mb-2">' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="vas-invmatch-DataElementValue">' + VIS.Msg.getMsg("VAS_Price") + '</span>' +
                                        '</div>' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="' + dataElementValueClass + '" title="' + VIS.Msg.getMsg("VAS_OrderPrice") + '">' + (data[i].OrderPrice).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].InvoicePriceListPrecision, maximumFractionDigits: data[i].InvoicePriceListPrecision }) + '</span>' +
                                        '</div>' +
                                        '<div class="vas-scheduleDataElement vas-invmatch-setCol1Width">' +
                                        '<span class="' + dataElementValueClass + '" title="' + VIS.Msg.getMsg("VAS_InvoicePrice") + '">' + (data[i].InvoicePrice).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].InvoicePriceListPrecision, maximumFractionDigits: data[i].InvoicePriceListPrecision }) + '</span>' +
                                        '</div>' +
                                        '</div>';

                                    TabPaneldesign += '<div class="vas-invmatch-Data-sglItem vas-invmatch-DataRow mb-2" style="background-color:white">' +
                                        '<div class="vas-scheduleDataElement">' +
                                        '<span class="vas-invmatch-Qty">' + VIS.Msg.getMsg("VAS_Match-QtyOrder") + (data[i].QtyOrdered).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) +
                                        ';' + VIS.Msg.getMsg("VAS_Match-QtyDeliver") + (data[i].OrderDelivered).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) +
                                        ';' + VIS.Msg.getMsg("VAS_Match-QtyInvoice") + (data[i].QtyInvoiced).toLocaleString(window.navigator.language, { minimumFractionDigits: data[i].UOMPrecision, maximumFractionDigits: data[i].UOMPrecision }) +
                                        '</span>' +
                                        '</div></div>';
                                }

                                TabPaneldesign += '</div></div>';

                                //Appending design to wrapperDiv
                                wrapperDiv.find('#VAS-ScheduleData_' + $self.windowNo).append(TabPaneldesign);
                            }
                            $root.find('.vas-invmatch-data-Paging').show();
                            resetPageCtrls(TotalPageCount);

                        }
                    }
                    else {
                        wrapperDiv.append('<div class="vas-igwidg-notfounddiv" id="vas_norecordcont_' + $self.windowNo + '">' + VIS.Msg.getMsg("VAS_NoMatchDataFound") + '</div>');
                        $root.find('.vas-invmatch-data-Paging').hide();
                    }
                    $bsyDiv[0].style.visibility = "hidden";
                },
                error: function (eror) {
                    $bsyDiv[0].style.visibility = "hidden";
                    console.log(eror);
                }
            })
        }

        /*This function will show busy indicator in widget */
        function createBusyIndicator() {
            $bsyDiv = $("<div class='vis-apanel-busy'>");
            $bsyDiv.css({
                "position": "absolute", "width": "98%", "height": "97%", 'text-align': 'center', 'z-index': '999'
            });
            $bsyDiv[0].style.visibility = "visible";
            $root.append($bsyDiv);
        };

        this.getRoot = function () {
            return $root;
        };

        VAS.VAS_InvoiceMatchedTabPanel.prototype.startPanel = function (windowNo, curTab) {
            this.windowNo = windowNo;
            this.curTab = curTab;
            $self.windowNo = windowNo;
            this.init();
        };
        /* This function to update tab panel based on selected record */
        VAS.VAS_InvoiceMatchedTabPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            this.record_ID = recordID;
            C_Invoice_ID = recordID;
            pageNo = 1;
            pageSize = 10;
            this.selectedRow = selectedRow;
            this.getInvoiceLineData(recordID);
        };
        /* This will set width as per window width in dafault case it is 75% */
        VAS.VAS_InvoiceMatchedTabPanel.prototype.sizeChanged = function (width) {
            this.panelWidth = width;
        };
        /* Disposing all variables from memory */
        VAS.VAS_InvoiceMatchedTabPanel.prototype.dispose = function () {
            this.record_ID = 0;
            this.windowNo = 0;
            this.curTab = null;
            this.rowSource = null;
            this.panelWidth = null;
            lblTitle = null;
        }
    }
})(VAS, jQuery);