/************************************************************
 * Module Name    : VAS
 * Purpose        : Created widget to get data for expected invoices
 * chronological  : Development
 * Created Date   : 11 September 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_ExpectedInvoiceWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $dummDiv = null;
        var $self = this;
        var ctx = VIS.Env.getCtx();
        var filetrDivExpectedInv = null;
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
        var arrayPageSize = 4;
        var countRecord = 4;
        var C_BPartner_ID = null;
        var $root = $('<div class="h-100 w-100">');
        var $maindiv = null;
        var gridDataResult = null;
        var TotalAmtArray = [];
        var ExpectedListDiv = null;
        var ColumnIds = null;
        var totalRecordCount = null;
        var listDesign = null;
        var cmbDocType = null;
        var invoiceRef = null;
        var $isGenChargesLabel = null;
        var $CreateInvoiceHeader = null;
        var $FilterHeaderforInv = null;
        var IsFilterBtnClicked = false;
        var isOrder = false;
        var vSearchBPartner = null;
        var ColumnIds = null;
        var totalRecordCount = null;
        var IsGenInvBtnClicked = false;
        var listDesign = null;
        var ListValue = null;
        var fromDate = null;
        var toDate = null;
        var ColumnData = [
            { refernceName: "VAS_ExpectedInvoiceList" },
            { ColumnName: "C_BPartner_ID" }
        ];

        /*Intialize function will intialize busy indiactor*/
        this.initalize = function () {
            GetColumnID();
            createDummyDiv();
            createBusyIndicator();
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);
            $maindiv = $('<div class="vas-exinvd-expected-invoice">')
            var headingDiv = $('<div class="vas-exinvd-expected-heading">');
            var filterDiv = $('<h6>' + VIS.Msg.getMsg("VAS_ExpectedInvoice") + '</h6>');
            filetrDivExpectedInv = $("<div class='vas-exinvd-filter dropdown'>" +
                "<div class='vas-exinvd-icondiv'>" +
                "<span class='vas-exinvd-filterspn btn d-flex position-relative' type='button' id='vas_exinvd_dropdownMenu_" + $self.windowNo + "'>" +
                "<i class='fa fa-filter vas-exinvd-filterIcon'></i>" +
                "</span>" +
                "</div>" +
                "<div class='vas-exinvd-filterPopupWrap' id='vas_exinvd_FilterPopupWrap_" + $self.windowNo + "'>" +
                "</div>" +
                "</div>");
            headingDiv.append(filterDiv).append(filetrDivExpectedInv);
            /*finding the div of filter icon*/
            var FilterToClickDiv = filetrDivExpectedInv.find("#vas_exinvd_dropdownMenu_" + $self.windowNo);
            /*Created design to filter records on click of filter icon*/
            FilterToClickDiv.on("click", function () {
                CreateDesignForFilter();
            });
            $maindiv.append(headingDiv);
        };

        /*This function will load data in widget */
        this.intialLoad = function () {
            $bsyDiv[0].style.visibility = "visible";
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetExpectedInvoiceData", {
                "ISOtrx": VIS.Env.getCtx().isSOTrx($self.windowNo), "pageNo": pageNo, "pageSize": pageSize,
                "ListValue": ListValue, "C_BPartner_ID": C_BPartner_ID, "fromDate": fromDate, "toDate": toDate
            }, function (dr) {
                TotalAmtArray = dr;
                if (TotalAmtArray != null && TotalAmtArray.length > 0) {

                    //InitailizeMessage();
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
                $('<div class="vas-exinvd-invoices-box vas-exinvd-emptydiv">' +
                    ' <h6></h6>' +
                    '<div class="vas-exinvd-invoices-detail">' +
                    '<div class="vas-exinvd-thumb-txt">' +
                    '<img src="" alt="" style="width:0px">' +
                    '<div class="vas-exinvd-company-w-date">' +
                    '<div class="vas-exinvd-com-name"></div>' +
                    '<div class="vas-exinvd-invoiceDate"></div>' +
                    '</div>' +
                    '</div>' +
                    '<div class="vas-exinvd-invoice-w-amount">' +
                    '<div class="vas-exinvd-invoice-lbl"></div>' +
                    '<div class="vas-exinvd-invoiceTotalAmt"><span></span></div>' +
                    '</div>' +
                    '</div>' +
                    '</div>');
        };
        /*This Function is used to create the design for the fiteration of data*/
        function CreateDesignForFilter() {
            //Created LOV control for task which will have billable,non billable and null values

            if (!IsFilterBtnClicked && !IsGenInvBtnClicked) {
                //Disabled filter icon after clicking it
                filetrDivExpectedInv.addClass('vas-disableArrow');
                IsFilterBtnClicked = true;
                $FilterHeaderforInv = $(
                    '<div class="vas-exinvd-main-filter-flyout">');

                var lookUpData = (VIS.Env.getCtx().isSOTrx($self.windowNo) == true ? "AD_Ref_List.Value IN ('AL','SO','DO')" : "AD_Ref_List.Value IN ('AL','PO','GR')");
                ExpectedListDiv = $('<div class="VAS-ExpectedListDiv" style="padding-bottom: 11px;">');
                $ExpectedListDiv = $('<div class="input-group vis-input-wrap">');
                /* parameters are: context, windowno., coloumn id, display type, DB coloumn name, Reference key, Is parent, Validation Code*/
                $ExpectedListLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.List, "VAS_ExpectedInvoiceList", ColumnIds.VAS_ExpectedInvoiceList, false, lookUpData);
                // Parameters are: columnName, mandatory, isReadOnly, isUpdateable, lookup,display length
                $self.vExpectedList = new VIS.Controls.VComboBox("VAS_ExpectedInvoiceList", true, false, true, $ExpectedListLookUp, 20);
                $self.vExpectedList.setValue("AL");
                var $ExpectedListControlWrap = $('<div class="vis-control-wrap">');
                $ExpectedListDiv.append($ExpectedListControlWrap);
                $ExpectedListControlWrap.append($self.vExpectedList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label style="background-color: #fff;">' + VIS.Msg.getMsg("VAS_ExpectedList") + '</label>');
                $ExpectedListDiv.append($ExpectedListControlWrap);
                ExpectedListDiv.append($ExpectedListDiv);

                //Created customer search control to filter out data
                var BPValidation = (VIS.Env.getCtx().isSOTrx($self.windowNo) == true ? "C_BPartner.IsActive='Y' AND C_BPartner.IsCustomer = 'Y' AND C_BPartner.IsSummary = 'N'"
                    : "C_BPartner.IsActive='Y' AND C_BPartner.IsVendor = 'Y' AND C_BPartner.IsSummary = 'N'");
                var BPartnerDiv = $('<div class="vas-BPartnerDiv">');
                $BPartnerDiv = $('<div class="input-group vis-input-wrap">');
                var BPartnerLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, ColumnIds.C_BPartner_ID, VIS.DisplayType.Search, "C_BPartner_ID", 0, false, BPValidation);
                vSearchBPartner = new VIS.Controls.VTextBoxButton("C_BPartner_ID", false, false, true, VIS.DisplayType.Search, BPartnerLookUp);
                var $BPartnerControlWrap = $('<div class="vis-control-wrap">');
                var $BPartnerButtonWrap = $('<div class="input-group-append">');
                $BPartnerDiv.append($BPartnerControlWrap);
                $BPartnerControlWrap.append(vSearchBPartner.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label style="background-color: transparent;">'
                    + (VIS.Env.getCtx().isSOTrx($self.windowNo) == true ? VIS.Msg.getMsg("VAS_CustomerPartner") : VIS.Msg.getMsg("VAS_VendorPartner")) + '</label>');
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
                $FilterHeaderforInv.append(ExpectedListDiv).append(BPartnerDiv).append($DatesDiv);

                var ButtonDiv = $('<div class="vas-expay-btndiv">');
                var ApplyBtn = $('<div class="vas-flyout-footer">' +
                    '<button id="VAS_Apply_' + $self.windowNo + '" class="VIS_Pref_btn-2 vas-expay-filtbtn">' + VIS.Msg.getMsg("VAS_Apply") + '</button>' +
                    '</div>'
                );
                var ClearBtn = $('<div class="vas-flyout-footer">' +
                    '<button id="VAS_Close_' + $self.windowNo + '" class="VIS_Pref_btn-2 vas-expay-filtbtn">' + VIS.Msg.getMsg("VAS_Clear") + '</button>' +
                    '</div>'
                );
                ButtonDiv.append(ClearBtn).append(ApplyBtn);
                $FilterHeaderforInv.append(ButtonDiv);
                $maindiv.append($FilterHeaderforInv);
                //Resetting the value on click of filetr button
                if (ListValue != null || C_BPartner_ID != null || fromDate != null || toDate != null) {
                    $self.vExpectedList.setValue(ListValue);
                    vSearchBPartner.setValue(C_BPartner_ID);
                    $FromDate.setValue(fromDate);
                    $ToDate.setValue(toDate);
                }
                var $ApplyButton = ApplyBtn.find("#VAS_Apply_" + $self.windowNo);
                vSearchBPartner.fireValueChanged = function () {
                    C_BPartner_ID = vSearchBPartner.value;
                };
                //on click of apply button filtering the data
                $ApplyButton.on("click", function () {
                    if ($FromDate.getValue() > $ToDate.getValue()) {
                        VIS.ADialog.info('VAS_PlzEnterCorrectDate');
                        $ToDate.setValue(null);
                        return;
                    }
                    $maindiv.find('#vas_listContainer_' + widgetID).remove();
                    $maindiv.find('#vas_arrawcontainer_' + widgetID).remove();
                    $maindiv.find('#vas_norecordcont_' + widgetID).remove();
                    ListValue = $self.vExpectedList.getValue();
                    fromDate = $FromDate.getValue();
                    toDate = $ToDate.getValue();
                    IsFilterBtnClicked = false;
                    pageNo = 1;
                    pageNoarray = 1;
                    CurrentPage = 1;
                    pageSize = 500;
                    arrayPageSize = 4;
                    RecCount = 0;
                    countRecord = 4;
                    $self.intialLoad();
                    if ($CreateInvoiceHeader != null) {
                        $CreateInvoiceHeader[0].remove();
                    }
                    $FilterHeaderforInv[0].remove();
                });
                ClearBtn.on('click', function () {
                    //Set value of list as ALL
                    if ($self.vExpectedList.oldValue == "AL") {
                        $self.vExpectedList.setValue(null);
                    }
                    $self.vExpectedList.setValue("AL");
                    vSearchBPartner.setValue(null);
                    $FromDate.setValue(null);
                    $ToDate.setValue(null);
                    C_BPartner_ID = null;
                    ListValue = null;
                    fromDate = null;
                    toDate = null;
                    if ($CreateInvoiceHeader != null) {
                        $CreateInvoiceHeader[0].remove();
                    }
                });
                //Enabled filter icon
                setTimeout(function () {
                    filetrDivExpectedInv.removeClass('vas-disableArrow');
                }, 2000);
            }
            else {
                $FilterHeaderforInv[0].remove();
                IsFilterBtnClicked = false;
            }
        }
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
            listDesign = $('<div class="vas-exinvd-invoicesListing" id="vas_listContainer_' + widgetID + '">');

            // Iterate through each item in the gridDataResult
            for (var i = 0; i < gridDataResult.length; i++) {
                RecCount = RecCount + 1;
                totalRecordCount = gridDataResult[0].recordCount;
                //var custChar = '';
                //var custNameArr = gridDataResult[i].Name.trim().split(' ');

                //// Generate customer initials
                //custChar = custNameArr[0].substring(0, 1).toUpperCase();
                //if (custNameArr.length > 1) {
                //    custChar += custNameArr[custNameArr.length - 1].substring(0, 1).toUpperCase();
                //} else {
                //    custChar = custNameArr[0].substring(0, 2).toUpperCase();
                //}
                var headingText = '';

                // Determine the correct text based on conditions
                if (gridDataResult[i].RecordType === "GRN") {
                    isOrder = false;
                    if (VIS.Env.getCtx().isSOTrx($self.windowNo) == true) {
                        headingText = VIS.Msg.getMsg("VAS_DeliveryOrder");
                    } else {
                        headingText = VIS.Msg.getMsg("VAS_GRN");
                    }
                } else if (gridDataResult[i].RecordType === "Order") {
                    isOrder = true;
                    if (VIS.Env.getCtx().isSOTrx($self.windowNo) == true) {
                        headingText = VIS.Msg.getMsg("VAS_SO");
                    } else {
                        headingText = VIS.Msg.getMsg("VAS_PO");
                    }
                }
                // Create the widget data design element
                var widgetDataDesign = '<div class="vas-exinvd-invoices-box">' +
                    '<div class="vas-exinvd-amtdiv">' +
                    '<h6 class="vas-exinvd-trxtype vas-exinvd-ovrflow" title="' + VIS.Msg.getMsg("VAS_Type") + ': ' + headingText + '">' + headingText + '</h6>';
                /*if document is GRN then add generate invoice button*/
                if (!isOrder) {
                    widgetDataDesign += '<span id="VAS_GenerateInvoice_' + widgetID + '" class="VAS-exinvd-generate-delivery-btn" data-grnid="' + gridDataResult[i].Record_ID
                        + '" data-windowid="' + gridDataResult[i].InvWinID + '" data-totalamt="' + gridDataResult[i].TotalAmt + '" data-bpname="' + gridDataResult[i].Name
                        + '" data-documentno="' + gridDataResult[i].DocumentNo + '" data-stdprecision="' + gridDataResult[i].stdPrecision
                        + '" data-isfullydelivered="' + gridDataResult[i].IsFullyDelivered + '" data-ad_org_id="' + gridDataResult[i].AD_Org_ID + '" title="' + VIS.Msg.getMsg("VAS_GenerateInvoice") + '">' +
                        '<i class="vis vis-action" ></i ></span>';
                }
                widgetDataDesign += '</div>' +
                    '<div class="vas-exinvd-invoices-detail">' +
                    '<div class="vas-exinvd-thumb-w-txt">';

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
                    '<div class="vas-exinvd-company-w-date">' +
                    '<div class="vas-exinvd-com-name vas-exinvd-ovrflow" title="' + (VIS.Env.getCtx().isSOTrx($self.windowNo) == true ? VIS.Msg.getMsg("VAS_CustomerPartner") : VIS.Msg.getMsg("VAS_VendorPartner")) + ': ' + gridDataResult[i].Name + '">' + gridDataResult[i].Name + '</div>' +
                '<div class="vas-exinvd-invoiceDate vas-exinvd-ovrflow" title="' + (isOrder == true ? VIS.Msg.getMsg("DateOrdered") : VIS.Msg.getMsg("DateDelivered")) + ': ' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].OrderdDate).toLocaleDateString() + '">' + (isOrder == true ? VIS.Msg.getMsg("DateOrdered") : VIS.Msg.getMsg("DateDelivered")) + ': ' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].OrderdDate).toLocaleDateString() + '</div>' +
                    '<div class="vas-exinvd-invoiceDate vas-exinvd-ovrflow" title="' + VIS.Msg.getMsg("DatePromised") + ': ' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].DatePromised).toLocaleDateString() + '">' +  VIS.Msg.getMsg("DatePromised") + ': ' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].DatePromised).toLocaleDateString() + '</div>' +
                    '</div>' +
                    '</div>' +
                    '<div class="vas-exinvd-invoice-w-amount" >' +
                    '<div class="vas-exinvd-invoice-lbl vas-exinvd-right-ovrflow" title="' + VIS.Msg.getMsg("DocumentNo") + ': ' + gridDataResult[i].DocumentNo + '">' + gridDataResult[i].DocumentNo + '</div>' +
                    '<span>' +
                    '<i class="glyphicon glyphicon-zoom-in" data-Record_ID="' + gridDataResult[i].Record_ID + '" data-windowId="' + gridDataResult[i].Window_ID +
                    '" data-Primary_ID="' + gridDataResult[i].Primary_ID + '" id="VAS-unAllocatedZoom_' + $self.windowNo + '" title="' + VIS.Msg.getMsg("VAS_Zoom") + '"></i>' +
                    '</span>' +
                    '</div>' +
                    '</div>';

                widgetDataDesign += '<div class="vas-exinvd-bottomdiv">' +
                    '<div class="vas-exinvd-invoiceTotalAmt vas-exinvd-ovrflow"  title="' + VIS.Msg.getMsg("VAS_Amount") + ':' + (gridDataResult[i].TotalAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: gridDataResult[i].stdPrecision, maximumFractionDigits: gridDataResult[i].stdPrecision }) + '">' + (gridDataResult[i].Symbol.length != 3 ? '<span>' + gridDataResult[i].Symbol + ' ' + '</span>' : '') + (gridDataResult[i].TotalAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: gridDataResult[i].stdPrecision, maximumFractionDigits: gridDataResult[i].stdPrecision }) +
                    (gridDataResult[i].Symbol.length == 3 ? ' ' + '<span>' + gridDataResult[i].Symbol + '</span>' : '') + '</div>';
                /*if invoice rule has value then append the same*/
                if (VIS.Env.getCtx().isSOTrx($self.windowNo) == true && gridDataResult[i].InvoiceRule != "") {
                    widgetDataDesign +=
                        '<div class="vas-exinvd-com-name vas-exinvd-right-ovrflow vas-exinvd-rulefont" title="' + VIS.Msg.getMsg("VAS_InvoiceRule") + ': ' + gridDataResult[i].InvoiceRule + '">' + gridDataResult[i].InvoiceRule + '</div>';
                }
                widgetDataDesign += '</div>' +
                    '</div>';

                // Append the widget data design to the list container
                listDesign.append(widgetDataDesign);
            }
            //This function used to generate invoice
            $root.off('click', '#VAS_GenerateInvoice_' + widgetID);
            $root.on('click', '#VAS_GenerateInvoice_' + widgetID, function () {
                var isFullyDelivered = VIS.Utility.Util.getValueOfString($(this).attr("data-isfullydelivered"));
                if (isFullyDelivered == "Y") {
                    VIS.ADialog.info("VAS_OrderIsNotFullyDelivered");
                    return;
                }
                var grnid = VIS.Utility.Util.getValueOfInt($(this).attr("data-grnid"));
                var windowId = VIS.Utility.Util.getValueOfInt($(this).attr("data-windowid"));
                var documentNo = VIS.Utility.Util.getValueOfString($(this).attr("data-documentno"));
                var totalAmt = parseFloat($(this).attr("data-totalamt"));
                var ad_org_id = VIS.Utility.Util.getValueOfInt($(this).attr("data-ad_org_id"));
                var bpName = VIS.Utility.Util.getValueOfString($(this).attr("data-bpname"));
                var stdPrecision = VIS.Utility.Util.getValueOfInt($(this).attr("data-stdprecision"));
                GetGRNParameter(grnid, windowId, documentNo, totalAmt, bpName, stdPrecision, ad_org_id);
            });
            /*if the record are less then 4 than append empty div*/
            if (RecCount < arrayPageSize) {
                bindDummyDiv(arrayPageSize - RecCount);
            }
            TotalPagesofrecords = Math.ceil(totalRecordCount / arrayPageSize);
            var arrowDiv = $(
                '<div class="vas-exinvd-pagingdiv">' +
                '<div class="vas-exinvd-slider-arrows" id="vas_arrawcontainer_' + widgetID + '">' +
                '<i class= "fa fa-arrow-circle-left" aria-hidden="true"></i>' +
                '<span class="vas-exinvd-pagespan">' + CurrentPage + ' ' + VIS.Msg.getMsg("VAS_Of") + ' ' + TotalPagesofrecords + '</span > ' +
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
                    countRecord = 4;
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
            //handled zoom here
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
        /*This function used translate message*/
        //function InitailizeMessage() {
        //    /*Inisalised Element for array*/
        //    var elements = [
        //        "VAS_ExpectedInvoice",
        //        "VAS_DeliveryOrder",
        //        "VAS_GRN",
        //        "VAS_PO",
        //        "VAS_SO",
        //        "VAS_Of",
        //        "VAS_FilterBy"
        //    ];

        //    VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);

        //}
        /**
        * This function is used to get the refernce id of list
        */
        var GetColumnID = function () {
            ColumnIds = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetColumnIDForExpPayment", { "refernceName": JSON.stringify(ColumnData) }, null);
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
            pageNo = 1;
            pageNoarray = 1;
            pageSize = 500;
            CurrentPage = 1;
            RecCount = 0;
            arrayPageSize = 4;
            countRecord = 4;
            $self.intialLoad();
        };
        /**
         * This Function is used to show process parameters
         * @param {any} grnid
         * @param {any} windowId
         *@param {any} documentNo
         * @param {any} totalAmt
         * @param {any} bpName
         * @param {any} stdPrecision
         */
        function GetGRNParameter(grnid, windowId, documentNo, totalAmt, bpName, stdPrecision, ad_org_id) {
            if (!IsGenInvBtnClicked && !IsFilterBtnClicked) {
                ctx.setContext($self.windowNo, "AD_Org_ID", ad_org_id);
                IsGenInvBtnClicked = true;
                var headingDiv = $('<div class="vas-exinvd-createinvdiv">' + VIS.Msg.getMsg("VAS_CreateInvoice") + '</div>');
                //    '<div class="vas-exinvd-filter-heading">' +
                //    '<h6>' + VIS.Msg.getMsg("VAS_DocumentNo")+':'+ documentNo + '</h6>' +
                //    '<h6>' + (VIS.Env.getCtx().isSOTrx($self.windowNo) == true ? VIS.Msg.getMsg("VAS_CustomerPartner") : VIS.Msg.getMsg("VAS_VendorPartner")) + ': '+ bpName + '</h6>' +
                //    '<h6>' + VIS.Msg.getMsg("VAS_Amount") + ':'+ parseFloat(totalAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision }) + '</h6>' +
                //    '</div>'+
                //'</div>');
                $CreateInvoiceHeader = $('<div class="vas-exinvd-filter-flyout">');

                DocTypeDiv = $('<div class="vas-exinvd-DocTyperDiv">');
                var $DocTypeDiv = $('<div class="input-group vis-input-wrap">');
                var DoctypeValidationCode = "C_DocType.DocBaseType IN ('API','APC','ARI','ARC') AND C_DocType.IsSOTrx='@IsSOTrx@' AND C_DocType.IsReturnTrx='@IsReturnTrx@'" +
                    " AND C_DocType.AD_Org_ID IN(0, @AD_Org_ID@) AND C_DocType.IsExpenseInvoice = 'N' AND C_DocType.IsActive = 'Y'";
                var DocTypelookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.TableDir, "C_DocType_ID", 0, false, DoctypeValidationCode);
                cmbDocType = new VIS.Controls.VComboBox("C_DocType_ID", true, false, true, DocTypelookUp, 50, VIS.DisplayType.TableDir);
                var $DocTypeControlWrap = $('<div class="vis-control-wrap">');
                var $DocTypeButtonWrap = $('<div class="input-group-append">');
                $DocTypeDiv.append($DocTypeControlWrap);
                $DocTypeControlWrap.append(cmbDocType.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label style="background-color: #fff;">' + VIS.Msg.getMsg("VAS_DoctType") + '</label>');
                $DocTypeDiv.append($DocTypeControlWrap);
                $DocTypeButtonWrap.append(cmbDocType.getBtn(0));
                $DocTypeDiv.append($DocTypeButtonWrap);
                DocTypeDiv.append($DocTypeDiv);

                invoiceRefDiv = $('<div class="vas-invoiceRefrDiv">');
                var $invoiceRefDiv = $('<div class="input-group vis-input-wrap">');
                invoiceRef = new VIS.Controls.VTextBox("Name", true, false, true, VIS.DisplayType.Textbox);
                var $invoiceRefControlWrap = $('<div class="vis-control-wrap">');
                var $invoiceRefButtonWrap = $('<div class="input-group-append">');
                $invoiceRefDiv.append($invoiceRefControlWrap);
                $invoiceRefControlWrap.append(invoiceRef.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label style="background-color: transparent;">' + VIS.Msg.getMsg("VAS_InvoiceRefernce") + '</label>');
                $invoiceRefDiv.append($invoiceRefControlWrap);
                //  $invoiceRefButtonWrap.append($self.cmbinvoiceRef.getBtn(0));
                $invoiceRefDiv.append($invoiceRefButtonWrap);
                invoiceRefDiv.append($invoiceRefDiv);

                isGenChargesdiv = $("<div class='vis-col'>");
                var $isGenChargesDiv = $('<div class="input-group vis-input-wrap">');
                // Parameters are: value, name, isMandatory
                $isGenChargesLabel = new VIS.Controls.VLabel("GenerateCharges", "GenerateCharges", false, true);
                // Parameters are:  columnName, isMandatory, isReadOnly, isUpdateable, displayType, title
                $isGenChargescheckbox = new VIS.Controls.VCheckBox("GenerateCharges", true, false, true, VIS.DisplayType.CheckBox, "CheckBox");
                var $isGenChargesControlWrap = $('<div class="vis-control-wrap">');
                var $isGenChargesButtonWrap = $('<div class="input-group-append">');
                $isGenChargesDiv.append($isGenChargesControlWrap);
                $isGenChargesControlWrap.append($isGenChargescheckbox.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("VAS_GenerateCharges") + '</label>');
                $isGenChargesDiv.append($isGenChargesControlWrap);
                //  $isGenChargesButtonWrap.append($self.cmbisGenCharges.getBtn(0));
                $isGenChargesDiv.append($isGenChargesButtonWrap);
                isGenChargesdiv.append($isGenChargesDiv);
                if (VIS.Env.getCtx().isSOTrx($self.windowNo) == true) {
                    $CreateInvoiceHeader.append(headingDiv).append(DocTypeDiv);
                }
                else {
                    $CreateInvoiceHeader.append(headingDiv).append(DocTypeDiv).append(invoiceRefDiv).append(isGenChargesdiv);
                }
                var ButtonDiv = $('<div class="vas-expay-btndiv">');
                var ApplyBtn = $('<div class="vas-flyout-footer">' +
                    '<button id="VAS_Apply_' + $self.windowNo + '" class="VIS_Pref_btn-2 vas-expay-filtbtn">' + VIS.Msg.getMsg("VAS_CreateInvoice") + '</button>' +
                    '</div>'
                );
                var CloseBtn = $('<div class="vas-flyout-footer">' +
                    '<button id="VAS_Close_' + $self.windowNo + '" class="VIS_Pref_btn-2 vas-expay-filtbtn">' + VIS.Msg.getMsg("VAS_Close") + '</button>' +
                    '</div>'
                );
                ButtonDiv.append(CloseBtn).append(ApplyBtn);
                $CreateInvoiceHeader.append(ButtonDiv);
                $maindiv.append($CreateInvoiceHeader);
                var $ApplyButton = ApplyBtn.find("#VAS_Apply_" + $self.windowNo);
                //here user is generating invoice
                $ApplyButton.on("click", function () {
                    if (cmbDocType.getValue() == null && (VIS.Env.getCtx().isSOTrx($self.windowNo) == false && invoiceRef.getValue() == "")) {
                        VIS.ADialog.info("VAS_DoctypeAndInvRefIsMandatory");
                        return;
                    }
                    if (cmbDocType.getValue() == null) {
                        VIS.ADialog.info("VAS_DoctypeIsMandatory");
                        return;
                    }
                    if (VIS.Env.getCtx().isSOTrx($self.windowNo) == false && invoiceRef.getValue() == "") {
                        VIS.ADialog.info("VAS_InvoiceReferenceIsMandatory");
                        return;
                    }
                    IsGenInvBtnClicked = false;
                    var invRef = invoiceRef.getValue();
                    var docId = cmbDocType.getValue();
                    var IsGenCheck = $isGenChargescheckbox.getValue();
                    generateInvoice(grnid, invRef, docId, IsGenCheck, windowId);
                });
                CloseBtn.on('click', function () {
                    IsGenInvBtnClicked = false;
                    $CreateInvoiceHeader[0].remove();
                });

            }
            else {
                IsGenInvBtnClicked = false;
                $CreateInvoiceHeader[0].remove();
            }

        };
        /**
         * This Function is used to generate invoice
         * @param {any} grnid
         * @param {any} invRef
         * @param {any} docId
         * @param {any} IsGenCheck
         * @param {any} windowId
         */
        function generateInvoice(grnid, invRef, docId, IsGenCheck, windowId) {
            $bsyDiv[0].style.visibility = "visible"
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/PoReceipt/GenerateInvoice",  // The URL endpoint
                type: "GET",  // You can also use "POST" if you need to send data in the body
                data: {
                    "grnid": grnid,  // Pass the grnid parameter
                    "invRef": invRef,  // Pass the invRef parameter
                    "docId": docId,  // Pass the docId parameter
                    "IsGenCheck": IsGenCheck  // Pass the IsGenCheck parameter
                },
                success: function (dr) {  // Callback function when the AJAX request succeeds
                    var responseData = JSON.parse(dr);  // The response (Invoice ID) from the server

                    if (responseData.C_Invoice_ID > 0) {
                        // If the Invoice_ID is greater than 0, handle zoom click and remove the filter header
                        handleZoomClick(responseData.C_Invoice_ID, windowId, "C_Invoice_ID");
                        $CreateInvoiceHeader[0].remove();
                    } else {
                        // If no invoice is created, show an info dialog
                        VIS.ADialog.info('','', responseData.ExceptionMessage);
                    }
                    // Hide the loading spinner once the operation is complete
                    $bsyDiv[0].style.visibility = "hidden";
                },
                error: function (xhr, status, error) {  // Callback function when the AJAX request fails
                    // In case of an error, show an info dialog and remove the filter header
                    VIS.ADialog.info(error);
                    // Hide the loading spinner if the request fails
                    $bsyDiv[0].style.visibility = "hidden";
                }
            });

        };
        /**
         * This Function is used to handle the zoom to window
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

    };

    VAS.VAS_ExpectedInvoiceWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_ExpectedInvoiceWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };
    /*this function is called when widget size changes*/
    VAS.VAS_ExpectedInvoiceWidget.prototype.widgetSizeChange = function (widget) {
        //this.getRoot().css("height", widget.height);
        //this.getRoot().css("width", widget.width);
        this.widgetInfo = widget;
    };
    /*Used to dispose the variable*/
    VAS.VAS_ExpectedInvoiceWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);