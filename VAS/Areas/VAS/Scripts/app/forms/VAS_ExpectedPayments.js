/************************************************************
 * Module Name    : VAS
 * Purpose        : Created widget to get data for expected Payments
 * chronological  : Development
 * Created Date   : 12 November 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_ExpectedPaymentWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $dummDiv = null;
        var $self = this;
        var ctx = VIS.Env.getCtx();
        var RecCount = 0;
        var widgetID = null;
        //this pgaeno will be responsible to get data from database
        var pageNo = 1;
        //this pgaeno will be responsible to get data from array
        var pageNoarray = 1;
        var CurrentPage = 1
        var TotalPagesofrecords = 1;
        //Main pagesize to get data in array
        var pageSize = 500;
        //Page size to show data in main div
        var arrayPageSize = 6;
        var countRecord = 6;
        var $root = $('<div class="h-100 w-100">');
        var $maindiv = null;
        var gridDataResult = null;
        var $FilterHeader = null
        var TotalAmtArray = [];
        var vSearchBPartner = null;
        var ColumnIds = null;
        var totalRecordCount = null;
        var listDesign = null;
        var FinancialPeriodValue = null;
        var fromDate = null;
        var toDate = null;
        var isSOTrx = null;
        var C_BPartner_ID = null;
        var IsFilterBtnClicked = false;
        var docTypeValue = null;
        var ColumnData = [
            { refernceName: "VAS_DocTypeExPayPaymentList" },
            { refernceName: "VAS_FinancialPeriodPaymentList" },
            { ColumnName: "C_BPartner_ID" }
        ];

        /*Intialize function will intialize busy indiactor*/
        this.initalize = function () {
            GetColumnID();
            createDummyDiv();
            createBusyIndicator();
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);
            /*If Screen is AR Receipt then IsSoTrx will be true else for AP Payment it will be false*/
            isSOTrx = VIS.context.getWindowContext($self.windowNo, "IsReceipt") == 'Y' ? true : false;
            $maindiv = $('<div class="vas-expay-expected-payment">')
            var headingDiv = $('<div class="vas-expay-expected-heading">');
            var HeadingLabelDiv = $('<div style="font-size:1.4em">' + (isSOTrx == true ? VIS.Msg.getMsg("VAS_ExpectedReceipt") : VIS.Msg.getMsg("VAS_ExpectedPayment")) + '</div>')
            var filetrDiv = $("<div class='vas-expay-filter dropdown'>" +
                "<div class='vas-expay-icondiv'>" +
                "<span class='vas-expay-filterspn btn d-flex position-relative' type='button' id='vas_expay_dropdownMenu_" + $self.windowNo + "'>" +
                "<i class='fa fa-filter vas-expay-filterIcon'></i>" +
                "</span>" +
                "</div>" +
                "<div class='vas-expay-filterPopupWrap' id='vas_expay_FilterPopupWrap_" + $self.windowNo + "'>" +
                "</div>" +
                "</div>");
            headingDiv.append(HeadingLabelDiv).append(filetrDiv);
            $maindiv.append(headingDiv);
            /*Find FilterDiv which will open filter section on click of filter button*/
            var FilterToClickDiv = filetrDiv.find("#vas_expay_dropdownMenu_" + $self.windowNo);
            FilterToClickDiv.on("click", function () {              
                if (!IsFilterBtnClicked) {
                    filetrDiv.addClass('vas-disableArrow');
                    IsFilterBtnClicked = true;
                    var $FilterDiv = $root.find("#vas_expay_FilterPopupWrap_" + $self.windowNo);
                    $FilterHeader = $(
                        '  <div class="vas-expay-filter-flyout">' +
                        '    <div class="vas-from-row">');

                    DocTypeExPayListDiv = $('<div class="VAS-DocTypeExPayListDiv">');
                    $DocTypeExPayListDiv = $('<div class="input-group vis-input-wrap">');
                    /* parameters are: context, windowno., coloumn id, display type, DB coloumn name, Reference key, Is parent, Validation Code*/
                    $DocTypeExPayListLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.List, "VAS_DocTypeExPayPaymentList", ColumnIds.VAS_DocTypeExPayPaymentList, false);
                    // Parameters are: columnName, mandatory, isReadOnly, isUpdateable, lookup,display length
                    $self.vDocTypeExPayList = new VIS.Controls.VComboBox("VAS_DocTypeExPayPaymentList", true, false, true, $DocTypeExPayListLookUp, 20);
                    $self.vDocTypeExPayList.setValue("01");
                    var $DocTypeExPayListControlWrap = $('<div class="vis-control-wrap">');
                    $DocTypeExPayListDiv.append($DocTypeExPayListControlWrap);
                    $DocTypeExPayListControlWrap.append($self.vDocTypeExPayList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label style="top:-9px;background-color: rgb(235, 235, 235);color:black;">' + VIS.Msg.getMsg("VAS_DocTypeExPayDiv") + '</label>');;
                    $DocTypeExPayListDiv.append($DocTypeExPayListControlWrap);
                    DocTypeExPayListDiv.append($DocTypeExPayListDiv);

                    FinancialPeriodListDiv = $('<div class="VAS-FinancialPeriodListDiv">');
                    $FinancialPeriodListDiv = $('<div class="input-group vis-input-wrap">');
                    /* parameters are: context, windowno., coloumn id, display type, DB coloumn name, Reference key, Is parent, Validation Code*/
                    $FinancialPeriodListLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.List, "VAS_FinancialPeriodPaymentList", ColumnIds.VAS_FinancialPeriodPaymentList, false);
                    // Parameters are: columnName, mandatory, isReadOnly, isUpdateable, lookup,display length
                    $self.vFinancialPeriodList = new VIS.Controls.VComboBox("VAS_FinancialPeriodPaymentList", false, false, true, $FinancialPeriodListLookUp, 20);
                    // $self.vFinancialPeriodList.setValue("01");
                    var $FinancialPeriodListControlWrap = $('<div class="vis-control-wrap">');
                    $FinancialPeriodListDiv.append($FinancialPeriodListControlWrap);
                    $FinancialPeriodListControlWrap.append($self.vFinancialPeriodList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label style="top:-9px;background-color: rgb(235, 235, 235);color:black;">' + VIS.Msg.getMsg("VAS_FinancialPeriodDiv") + '</label>');;
                    $FinancialPeriodListDiv.append($FinancialPeriodListControlWrap);
                    FinancialPeriodListDiv.append($FinancialPeriodListDiv);

                    //Created customer search control to filter out data
                    var BPartnerDiv = $('<div class="vas-BPartnerDiv">');
                    $BPartnerDiv = $('<div class="input-group vis-input-wrap">');
                    var BPartnerLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, ColumnIds.C_BPartner_ID, VIS.DisplayType.Search);
                    vSearchBPartner = new VIS.Controls.VTextBoxButton("C_BPartner_ID", false, false, true, VIS.DisplayType.Search, BPartnerLookUp);
                    var $BPartnerControlWrap = $('<div class="vis-control-wrap">');
                    var $BPartnerButtonWrap = $('<div class="input-group-append">');
                    $BPartnerDiv.append($BPartnerControlWrap);
                    $BPartnerControlWrap.append(vSearchBPartner.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label style="top:-9px;background-color: rgb(235, 235, 235);color:black;">' + VIS.Msg.getMsg("VAS_BussinessPartner") + '</label>');;
                    $BPartnerDiv.append($BPartnerControlWrap);
                    $BPartnerButtonWrap.append(vSearchBPartner.getBtn(0));
                    $BPartnerDiv.append($BPartnerButtonWrap);
                    BPartnerDiv.append($BPartnerDiv);

                    //Created from and to date control to filter out data
                    var FromDatediv = $('<div class="vas-expay-dateWidth vas-FromdateDiv">');
                    $FromDatewrapDiv = $('<div class="input-group vis-input-wrap">');
                    $FromDate = new VIS.Controls.VDate("DateReport", false, false, true, VIS.DisplayType.Date, "DateReport");
                    var $FromDateWrap = $('<div class="vis-control-wrap">');
                    $FromDatewrapDiv.append($FromDateWrap);
                    $FromDateWrap.append($FromDate.getControl().attr('placeholder', ' ').attr('data-placeholder', '')).append('<label class="vas-expay-lablels" style="color:black;">' + VIS.Msg.getMsg("VAS_FromDate") + '</label>');;
                    FromDatediv.append($FromDatewrapDiv);

                    var toDatediv = $('<div class="vas-expay-dateWidth vas-todateDiv">');
                    $toDatewrapDiv = $('<div class="input-group vis-input-wrap">');
                    $ToDate = new VIS.Controls.VDate("DateReport", false, false, true, VIS.DisplayType.Date, "DateReport");
                    var $toDateWrap = $('<div class="vis-control-wrap">');
                    $toDatewrapDiv.append($toDateWrap);
                    $toDateWrap.append($ToDate.getControl().attr('placeholder', ' ').attr('data-placeholder', '')).append('<label class="vas-expay-lablels" style="color:black;">' + VIS.Msg.getMsg("VAS_ToDate") + '</label>');;
                    toDatediv.append($toDatewrapDiv);
                    var $DatesDiv = $('<div class="vas-expay-datefilter">');
                    $DatesDiv.append(FromDatediv).append(toDatediv);
                    $FilterHeader.append(DocTypeExPayListDiv).append(FinancialPeriodListDiv).append(BPartnerDiv).append($DatesDiv);

                    var ButtonDiv = $('<div class="vas-expay-btndiv">');
                    var ApplyBtn = $('<div class="vas-flyout-footer">' +
                        '<button id="VAS_Apply_' + $self.windowNo + '" class="VIS_Pref_btn-2 vas-expay-filtbtn">' + VIS.Msg.getMsg("VAS_Apply") + '</button>' +
                        '</div>'
                    );
                    var CloseBtn = $('<div class="vas-flyout-footer">' +
                        '<button id="VAS_Close_' + $self.windowNo + '" class="VIS_Pref_btn-2 vas-expay-filtbtn">' + VIS.Msg.getMsg("VAS_Clear") + '</button>' +
                        '</div>'
                    );
                    ButtonDiv.append(CloseBtn).append(ApplyBtn);
                    $FilterHeader.append(ButtonDiv);
                    $maindiv.append($FilterHeader);
                    if (C_BPartner_ID != null || FinancialPeriodValue != null || fromDate != null || toDate != null || docTypeValue != null) {
                        $self.vFinancialPeriodList.setValue(FinancialPeriodValue);
                        vSearchBPartner.setValue(C_BPartner_ID);
                        $FromDate.setValue(fromDate);
                        $ToDate.setValue(toDate);
                        $self.vDocTypeExPayList.setValue(docTypeValue);
                    }
                    var $ApplyButton = ApplyBtn.find("#VAS_Apply_" + $self.windowNo);
                    vSearchBPartner.fireValueChanged = function () {
                        C_BPartner_ID = vSearchBPartner.value;
                    };
                    $ApplyButton.on("click", function () {
                        if (C_BPartner_ID == null && $self.vFinancialPeriodList.getValue() == null && $FromDate.getValue() == null && $ToDate.getValue() == null) {
                            VIS.ADialog.info("VAS_SelectAnyOneFilter");
                            return '';
                        }
                        if ($FromDate.getValue() > $ToDate.getValue()) {
                            VIS.ADialog.info('VAS_PlzEnterCorrectDate');
                            $ToDate.setValue(null);
                            return;
                        }
                        IsFilterBtnClicked = false;
                        pageNo = 1;
                        pageNoarray = 1;
                        CurrentPage = 1;
                        pageSize = 500;
                        arrayPageSize = 6;
                        RecCount = 0;
                        countRecord = 6;
                        FinancialPeriodValue = $self.vFinancialPeriodList.getValue()
                        fromDate = $FromDate.getValue();
                        toDate = $ToDate.getValue();
                        docTypeValue = $self.vDocTypeExPayList.getValue();
                        $FilterHeader.remove();
                        $maindiv.find('#vas_listContainer_' + widgetID).remove();
                        $maindiv.find('#vas_arrawcontainer_' + widgetID).remove();
                        $maindiv.find('#vas_norecordcont_' + widgetID).remove();
                        $self.intialLoad();
                    });
                    CloseBtn.on('click', function () {
                        if ($self.vFinancialPeriodList.oldValue == null) {
                            $self.vFinancialPeriodList.setValue(0);
                        }
                        $self.vFinancialPeriodList.setValue(null);
                        vSearchBPartner.setValue(null);
                        $FromDate.setValue(null);
                        $ToDate.setValue(null);
                        C_BPartner_ID = null;
                        FinancialPeriodValue = null;
                        fromDate = null;
                        toDate = null;
                        docTypeValue = null;
                    });
                    setTimeout(function () {
                        filetrDiv.removeClass('vas-disableArrow');
                    }, 1000);
                }
                else {
                    $FilterHeader.remove();
                    IsFilterBtnClicked = false;
                }               
            })          
        };

        /*This function will load data in widget */
        this.intialLoad = function () {
            $bsyDiv[0].style.visibility = "visible";
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetExpectedPaymentData", {
                "ISOtrx": isSOTrx, "pageNo": pageNo, "pageSize": pageSize, "FinancialPeriodValue": FinancialPeriodValue,
                "C_BPartner_ID": C_BPartner_ID, "fromDate": fromDate, "toDate": toDate, "docTypeValue": docTypeValue
            }, function (dr) {
                TotalAmtArray = dr;
                if (TotalAmtArray != null && TotalAmtArray.length > 0) {
                    /*if on reverse arrow click pageNoarray is zero 
                     then initailzed page number to get previous record*/
                    if (pageNoarray == 0) {
                        pageNoarray = Math.ceil(TotalAmtArray.length / arrayPageSize);
                    }
                    /*this function is use to get records based on start and end index*/
                    gridDataResult = getRecordsForPage(TotalAmtArray, pageNoarray, arrayPageSize);
                    //this function creating the design
                    CreateListDesign();
                }
                else {
                    $maindiv.append('<div class="vas-igwidg-notfounddiv" id="vas_norecordcont_' + widgetID + '">' + VIS.Msg.getMsg("VAS_RecordNotFound") + '</div>')
                    $root.append($maindiv);
                }
                $bsyDiv[0].style.visibility = "hidden";
            });
        };
        /*this function will create empty div if records are less then 4*/
        function createDummyDiv() {
            $dummDiv =
                $('<div class="vas-expay-payments-box vas-expay-emptydiv">' +
                    ' <h6></h6>' +
                    '<div class="vas-expay-payments-detail">' +
                    '<div class="vas-expay-thumb-txt">' +
                    '<img src="" alt="" style="width:0px">' +
                    '<div class="vas-expay-company-w-date">' +
                    '<div class="vas-expay-com-name"></div>' +
                    '<div class="vas-expay-paymentDate"></div>' +
                    '</div>' +
                    '</div>' +
                    '<div class="vas-expay-payment-w-amount">' +
                    '<div class="vas-expay-payment-lbl"></div>' +
                    '<div class="vas-expay-paymentTotalAmt"><span></span></div>' +
                    '</div>' +
                    '</div>' +
                    '</div>');
        };
        function bindDummyDiv(j) {
            for (var i = 0; i < j; i++) {
                listDesign.append($dummDiv);
            };
        };
        /*This function will create the design */
        function CreateListDesign() {
            var hue = Math.floor(Math.random() * (360 - 0)) + 0;
            var v = Math.floor(Math.random() * (75 - 60 + 1)) + 60;
            var pastel = 'hsl(' + hue + ', 100%,' + v + '%)';
            listDesign = $('<div class="vas-expay-paymentsListing" id="vas_listContainer_' + widgetID + '">');

            // Iterate through each item in the gridDataResult
            for (var i = 0; i < gridDataResult.length; i++) {
                RecCount = RecCount + 1;
                totalRecordCount = gridDataResult[0].recordCount;
                var custChar = '';
                var custNameArr = gridDataResult[i].Name.trim().split(' ');

                // Generate customer initials
                custChar = custNameArr[0].substring(0, 1).toUpperCase();
                if (custNameArr.length > 1) {
                    custChar += custNameArr[custNameArr.length - 1].substring(0, 1).toUpperCase();
                } else {
                    custChar = custNameArr[0].substring(0, 2).toUpperCase();
                }
                var headingText = '';

                // Determine the correct text based on conditions
                if (gridDataResult[i].windowType === "Order") {
                    if (isSOTrx == true) {
                        headingText = VIS.Msg.getMsg("VAS_AdvSalesOrder");
                    } else {
                        headingText = VIS.Msg.getMsg("VAS_AdvPurchaseOrder");
                    }
                } else if (gridDataResult[i].windowType === "Invoice") {
                    if (isSOTrx == true) {
                        headingText = VIS.Msg.getMsg("VAS_SalesInvoice");
                    } else {
                        headingText = VIS.Msg.getMsg("VAS_PurchaseInvoice");
                    }
                }
                // Create the widget data design element
                var widgetDataDesign = '<div class="vas-expay-payments-box">' +
                    '<span class="VAS-expay-searchLinkIconBlock">' +
                    '<i class="glyphicon glyphicon-zoom-in" data-Record_ID="' + gridDataResult[i].Record_ID + '" data-windowId="' + gridDataResult[i].Window_ID + '" data-Primary_ID="' + gridDataResult[i].Primary_ID + '" id="VAS-unAllocatedZoom_' + $self.windowNo + '"></i>' +
                    '</span>' +
                    '<div class="vas-expay-payments-detail">' +
                    '<div class="vas-expay-payment-w-amount">' +
                    '<div class="vas-expay-payment-lbl vas-expay-com-name vas-expay-head-font">' + headingText + '</div>' +
                    '</div>' +
                    '<div class="vas-expay-payment-w-amount">' +
                    '<div class="vas-expay-payment-lbl vas-expay-text-align vas-expay-com-name" title="' + VIS.Msg.getMsg("VAS_DueDate") + ': ' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].OrderdDate).toLocaleDateString() + '">' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].OrderdDate).toLocaleDateString() + '</div>' +
                    '</div>' +
                    '<div class="vas-expay-payment-w-amount">';

                //if (gridDataResult[i].ImageUrl != null) {
                //    // Append image if available
                //    widgetDataDesign += '<img class="vas-businessPartnerImg" alt="' + gridDataResult[i].ImageUrl + '" src="' + VIS.Application.contextUrl + gridDataResult[i].ImageUrl + '">'
                //} else {

                //    // Append initial if image is not available
                //    widgetDataDesign +=
                //        '<div style="float:left; background-color:' + pastel + '" class="vas-igtwidg-img-icon">' +
                //        '<span style="font-size: 16px;">' + custChar + '</span>' +
                //        '</div>';
                //}
                widgetDataDesign +=
                    // '<div class="vas-expay-company-w-date">' +
                    '<div class="vas-expay-payment-lbl vas-expay-com-name" title="' + VIS.Msg.getMsg("VAS_BusinessPartner") + ': ' + gridDataResult[i].Name + '">' + gridDataResult[i].Name + '</div>' +
                    '</div>' +
                    '<div class="vas-expay-payment-w-amount">' +
                    '<div class="vas-expay-payment-lbl vas-expay-text-align vas-expay-com-name" title="' + VIS.Msg.getMsg("DocumentNo") + ': ' + gridDataResult[i].DocumentNo + '">' + gridDataResult[i].DocumentNo + '</div>' +
                    '</div>' +
                    '<div class="vas-expay-payment-w-amount">' +
                    '<div class="vas-expay-payment-lbl vas-expay-com-name" title="' + VIS.Msg.getMsg("VAS_ISO_Code") + ': ' + gridDataResult[i].ISO_Code + '">' + gridDataResult[i].ISO_Code + '</div>' +
                    '</div>' +
                    '<div class="vas-expay-payment-w-amount">' +
                    '<div class="vas-expay-payment-lbl vas-expay-text-align vas-expay-com-name" title="' + VIS.Msg.getMsg("VAS_DueAmtExPay") + ': ' + (gridDataResult[i].TotalAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: gridDataResult[i].stdPrecision, maximumFractionDigits: gridDataResult[i].stdPrecision }) + '">' + (gridDataResult[i].Symbol.length != 3 ? '<span class="vas-expay-symbol">' + gridDataResult[i].Symbol + ' ' + '</span>' : '') + (gridDataResult[i].TotalAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: gridDataResult[i].stdPrecision, maximumFractionDigits: gridDataResult[i].stdPrecision }) +
                    (gridDataResult[i].Symbol.length == 3 ? ' ' + '<span class="vas-expay-symbol">' + gridDataResult[i].Symbol + '</span>' : '') + '</div>' +
                    '</div>' +
                    '</div>' +
                    '</div>';

                // Append the widget data design to the list container
                listDesign.append(widgetDataDesign);
            }
            /*if the record are less then 4 than append empty div*/
            if (RecCount < arrayPageSize) {
                bindDummyDiv(arrayPageSize - RecCount);
            }
            TotalPagesofrecords = Math.ceil(totalRecordCount / arrayPageSize);
            var arrowDiv = $(
                '<div class="vas-expay-pagingdiv">' +
                '<div class="vas-expay-slider-arrows" id="vas_arrawcontainer_' + widgetID + '">' +
                '<i class= "fa fa-arrow-circle-left" aria-hidden="true"></i>' +
                '<span class="vas-expay-pagespan">' + CurrentPage + ' ' + VIS.Msg.getMsg("VAS_Of") + ' ' + TotalPagesofrecords + '</span > ' +
                '<i class="fa fa-arrow-circle-right" aria-hidden="true"></i>' +
                '</div>' +
                '</div>');
            $maindiv.append(listDesign).append(arrowDiv);
            $root.append($maindiv);
            //disabled the left array when page no's are 1
            if (pageNoarray == 1 && pageNo == 1) {
                arrowDiv.find(".fa-arrow-circle-left").addClass('vas-disableArrow');
            }
            //handled value on right array click to get next record
            arrowDiv.find(".fa-arrow-circle-right").on("click", function () {
                pageNoarray++;
                CurrentPage++
                $maindiv.find('#vas_listContainer_' + widgetID).remove();
                $maindiv.find('#vas_arrawcontainer_' + widgetID).remove();
                gridDataResult = getRecordsForPage(TotalAmtArray, pageNoarray, arrayPageSize);
                if (countRecord >= TotalAmtArray.length) {
                    pageNoarray = 1;
                    countRecord = 5;
                    pageNo++;
                    $self.intialLoad();
                }
                else {
                    countRecord = countRecord + arrayPageSize;
                    CreateListDesign();
                }
            });
            //handled value on left click to get previous record
            arrowDiv.find(".fa-arrow-circle-left").on("click", function () {
                pageNoarray--;
                CurrentPage--;
                $maindiv.find('#vas_listContainer_' + widgetID).remove();
                $maindiv.find('#vas_arrawcontainer_' + widgetID).remove();
                gridDataResult = getRecordsForPage(TotalAmtArray, pageNoarray, arrayPageSize);
                if (countRecord == arrayPageSize) {
                    countRecord = pageSize;
                    pageNo--;
                    $self.intialLoad();
                }
                else {
                    countRecord = countRecord - arrayPageSize;
                    CreateListDesign();
                }
            })
            //If all the records displayed then disabled next button
            var totalPages = Math.ceil(totalRecordCount / pageSize);
            if (pageNo >= totalPages && countRecord >= TotalAmtArray.length) {
                arrowDiv.find(".fa-arrow-circle-right").addClass('vas-disableArrow');
            }
            $maindiv.find('.glyphicon-zoom-in').on("click", function () {
                var Record_ID = VIS.Utility.Util.getValueOfInt($(this).attr("data-Record_ID"));
                var windowId = VIS.Utility.Util.getValueOfInt($(this).attr("data-windowId"));
                var Primary_ID = VIS.Utility.Util.getValueOfString($(this).attr("data-Primary_ID"));
                handleZoomClick(Record_ID, windowId, Primary_ID);
            })
        }
        /**
        * This Function is used to slice array of records according to start and end index
        * @param {any} records
        * @param {any} pageNumber
        * @param {any} pageSize
        */
        function getRecordsForPage(records, pageNumber, pageSize) {
            var startIndex = (pageNumber - 1) * pageSize;
            var endIndex = startIndex + pageSize;
            return records.slice(startIndex, endIndex);
        }
        /*This function will show busy indicator in widget */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv.css({
                "position": "absolute", "width": "98%", "height": "97%", 'text-align': 'center', 'z-index': '999'
            });
            $bsyDiv[0].style.visibility = "visible";
            $root.append($bsyDiv);
        };
        /**
        * This function is used to get the refernce id of list
        */
        var GetColumnID = function () {
            ColumnIds = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetColumnIDForExpPayment", { "refernceName": JSON.stringify(ColumnData) }, null);
        }
        /**
        * this fucntion is used to zoom to window for the record which is clicked from grid
        * @param {any} Record_ID
        * @param {any} windowId
        * @param {any} Primary_ID
        */
        function handleZoomClick(Record_ID, windowId, Primary_ID) {
            if (windowId > 0) {
                var zoomQuery = new VIS.Query();
                zoomQuery.addRestriction(Primary_ID, VIS.Query.prototype.EQUAL, Record_ID);
                zoomQuery.setRecordCount(1);
                VIS.viewManager.startWindow(windowId, zoomQuery);
            }
        }
        /*This function used to get root*/
        this.getRoot = function () {
            return $root;
        };
        /*this function is used to refresh design and data of widget*/
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            $maindiv.find('#vas_listContainer_' + widgetID).remove();
            $maindiv.find('#vas_arrawcontainer_' + widgetID).remove();
            $maindiv.find('#vas_norecordcont_' + widgetID).remove();
            C_BPartner_ID = null;
            FinancialPeriodValue = null;
            fromDate = null;
            toDate = null;
            docTypeValue = null;
            pageNo = 1;
            pageNoarray = 1;
            pageSize = 500;
            CurrentPage = 1;
            if ($FilterHeader != null) {
                $FilterHeader.remove();
            }
            RecCount = 0;
            arrayPageSize = 6;
            docTypeValue = null;
            countRecord = 6;
            $self.intialLoad();
        };
    };

    VAS.VAS_ExpectedPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var ssef = this;
        window.setTimeout(function () {
            ssef.intialLoad();
        }, 50);
    };

    VAS.VAS_ExpectedPaymentWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };
    /*this function is called when widget size changes*/
    VAS.VAS_ExpectedPaymentWidget.prototype.widgetSizeChange = function (widget) {
        //this.getRoot().css("height", widget.height);
        //this.getRoot().css("width", widget.width);
        this.widgetInfo = widget;
    };
    /*Used to dispose the variable*/
    VAS.VAS_ExpectedPaymentWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);