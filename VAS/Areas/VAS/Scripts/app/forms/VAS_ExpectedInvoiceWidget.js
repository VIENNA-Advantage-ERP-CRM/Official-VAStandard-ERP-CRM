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
        var $self = this;
        var ctx = VIS.Env.getCtx();
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
        var arrayPageSize = 5;
        var countRecord = 5;
        var $root = $('<div class="h-100 w-100">');
        var $maindiv = null;
        var gridDataResult = null;
        var TotalAmtArray = [];
        var ExpectedListDiv = null;
        var ColumnIds = null;
        var totalRecordCount = null;

        /*Intialize function will intialize busy indiactor*/
        this.initalize = function () {
            GetColumnID();
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);
            $maindiv = $('<div class="vas-exinvd-expected-invoice">')
            var headingDiv = $('<div class="vas-exinvd-expected-heading">');
            var filterDiv = $('<h6>' + VIS.Msg.getMsg("VAS_ExpectedInvoice") + '</h6 >' +
                '<div class="vas-exinvd-filterby-col">');
            //'<i class="fa fa-filter" aria-hidden="true"></i>');
            // '<div class="vas-exinvd-filter-label">'+ VIS.Msg.getMsg("VAS_FilterBy") +'</div>');
            headingDiv.append(filterDiv);
            //Created LOV control for task which will have billable,non billable and null values
            var lookUpData = (VIS.Env.getCtx().isSOTrx($self.windowNo) == true ? "AD_Ref_List.Value IN ('AL','SO','DO')" : "AD_Ref_List.Value IN ('AL','PO','GR')");
            ExpectedListDiv = $('<div class="VAS-ExpectedListDiv">');
            $ExpectedListDiv = $('<div class="input-group vis-input-wrap">');
            /* parameters are: context, windowno., coloumn id, display type, DB coloumn name, Reference key, Is parent, Validation Code*/
            $ExpectedListLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.List, "VAS_ExpectedInvoiceList", ColumnIds.AD_Reference_ID, false, lookUpData);
            // Parameters are: columnName, mandatory, isReadOnly, isUpdateable, lookup,display length
            $self.vExpectedList = new VIS.Controls.VComboBox("VAS_ExpectedInvoiceList", true, false, true, $ExpectedListLookUp, 20);
            $self.vExpectedList.setValue("AL");
            var $ExpectedListControlWrap = $('<div class="vis-control-wrap">');
            $ExpectedListDiv.append($ExpectedListControlWrap);
            $ExpectedListControlWrap.append($self.vExpectedList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("VAS_FilterBy") + '</label>');
            $ExpectedListDiv.append($ExpectedListControlWrap);
            ExpectedListDiv.append($ExpectedListDiv);
            headingDiv.append(ExpectedListDiv);
            $maindiv.append(headingDiv);
            $self.vExpectedList.fireValueChanged = function () {
                pageNo = 1;
                pageNoarray = 1;
                CurrentPage = 1;
                pageSize = 500;
                arrayPageSize = 5;
                countRecord = 5;
                $self.vExpectedList.setValue($self.vExpectedList.getValue());
                $maindiv.find('#vas_listContainer_' + widgetID).remove();
                $maindiv.find('#vas_arrawcontainer_' + widgetID).remove();
                $self.intialLoad();
            }
            createBusyIndicator();
            $bsyDiv[0].style.visibility = "visible";
        };

        /*This function will load data in widget */
        this.intialLoad = function () {
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetExpectedInvoiceData", { "ISOtrx": VIS.Env.getCtx().isSOTrx($self.windowNo), "pageNo": pageNo, "pageSize": pageSize, "ListValue": $self.vExpectedList.getValue() }, function (dr) {
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
                    $root.append($maindiv);
                }
                $bsyDiv[0].style.visibility = "hidden";
            });
        };
        /*This function will create the design */
        function CreateListDesign() {
            $bsyDiv[0].style.visibility = "visible";
            var hue = Math.floor(Math.random() * (360 - 0)) + 0;
            var v = Math.floor(Math.random() * (75 - 60 + 1)) + 60;
            var pastel = 'hsl(' + hue + ', 100%,' + v + '%)';
            var listDesign = $('<div class="vas-exinvd-invoicesListing" id="vas_listContainer_' + widgetID + '">');

            // Iterate through each item in the gridDataResult
            for (var i = 0; i < gridDataResult.length; i++) {
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
                if (gridDataResult[i].RecordType === "GRN") {
                    if (VIS.Env.getCtx().isSOTrx($self.windowNo) == true) {
                        headingText = VIS.Msg.getMsg("VAS_DeliveryOrder");
                    } else {
                        headingText = VIS.Msg.getMsg("VAS_GRN");
                    }
                } else if (gridDataResult[i].RecordType === "Order") {
                    if (VIS.Env.getCtx().isSOTrx($self.windowNo) == true) {
                        headingText = VIS.Msg.getMsg("VAS_SO");
                    } else {
                        headingText = VIS.Msg.getMsg("VAS_PO");
                    }
                }
                // Create the widget data design element
                var widgetDataDesign = '<div class="vas-exinvd-invoices-box">' +
                    '<h6>' + headingText + '</h6>' +
                    '<div class="vas-exinvd-invoices-detail">' +
                    '<div class="vas-exinvd-thumb-w-txt">';

                if (gridDataResult[i].ImageUrl != null) {
                    // Append image if available
                    widgetDataDesign += '<img class="vas-businessPartnerImg" alt="' + gridDataResult[i].ImageUrl + '" src="' + VIS.Application.contextUrl + gridDataResult[i].ImageUrl + '">'
                } else {

                    // Append initial if image is not available
                    widgetDataDesign +=
                        '<div style="float:left; background-color:' + pastel + '" class="vas-igtwidg-img-icon">' +
                        '<span style="font-size: 16px;">' + custChar + '</span>' +
                        '</div>';
                }
                widgetDataDesign +=
                    '<div class="vas-exinvd-company-w-date">' +
                    '<div class="vas-exinvd-com-name">' + gridDataResult[i].Name + '</div>' +
                    '<div class="vas-exinvd-invoiceDate">' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].OrderdDate).toLocaleDateString() + '</div>' +
                    '</div>' +
                    '</div>' +
                    '<div class="vas-exinvd-invoice-w-amount" >' +
                    '<div class="vas-exinvd-invoice-lbl">' + gridDataResult[i].DocumentNo + '</div>' +
                    '<div class="vas-exinvd-invoiceTotalAmt">' + (gridDataResult[i].Symbol.length != 3 ? '<span>' + gridDataResult[i].Symbol + ' ' + '</span>' : '') + (gridDataResult[i].TotalAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: gridDataResult[i].stdPrecision, maximumFractionDigits: gridDataResult[i].stdPrecision }) +
                    (gridDataResult[i].Symbol.length == 3 ? ' ' + '<span>' + gridDataResult[i].Symbol + '</span>' : '') + '</div>' +
                    '</div>' +
                    '</div>' +
                    '</div>';

                // Append the widget data design to the list container
                listDesign.append(widgetDataDesign);
            }
            TotalPagesofrecords = Math.ceil(totalRecordCount / arrayPageSize);
            var arrowDiv = $('<div class="vas-exinvd-slider-arrows" id="vas_arrawcontainer_' + widgetID + '">' +
                '<i class= "fa fa-arrow-circle-left" aria-hidden="true"></i>' +
                '<span class="vas-exinvd-pagespan">' + CurrentPage + ' ' + VIS.Msg.getMsg("VAS_Of") + ' ' + TotalPagesofrecords + '</span > ' +
                '<i class="fa fa-arrow-circle-right" aria-hidden="true"></i>' +
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
            $bsyDiv[0].style.visibility = "hidden";
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
            ColumnIds = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetColumnID", { "ColumnData": '' }, null);
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
            pageNo = 1;
            pageNoarray = 1;
            pageSize = 500;
            CurrentPage = 1;
            arrayPageSize = 5;
            countRecord = 5;
            $self.intialLoad();
        };
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