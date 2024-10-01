/************************************************************
 * Module Name    : VAS
 * Purpose        : Created Widget To get AR/AP Invoice Data 
 *                  of top 5 Business partner
 * chronological  : Development
 * Created Date   : 21 August 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_InvGrandTotalWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var ctx = VIS.Env.getCtx();
        var $YearBasedDataListLookUp = null;
        var widgetID = null;
        var unit = null;
        var $root = $('<div class="h-100 w-100">');
        var $maindiv = null;
        var TotalAmtArray = [];
        var culture = new VIS.CultureSeparator();
        var $dummDiv = null;
        var RecCount = 0;
        var listDesign = null;
        var ExpectedRecordCount = 5;

        /*Intialize function will intialize busy indiactor*/
        this.initalize = function () {
            //This function will get the reference id of list
            GetColumnID();
            createBusyIndicator();
            createDummyDiv()
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);
            var classTop5 = (VIS.Env.getCtx().isSOTrx($self.windowNo) == true ? 'vas-igtwidg-customer-bgColor' : 'vas-igtwidg-vendor-bgColor');
            $maindiv = $('<div class="vas-igtwidg-top-vendors-col ' + classTop5 + '">');
            //Getting list to fiter the data base on year
            var HeadingComboDiv = $('<div class="d-flex justify-content-between">');
            var HeadingDiv = $('<div class= "vas-igtwidg-vendors-heading">' + (VIS.Env.getCtx().isSOTrx($self.windowNo) == true ? VIS.Msg.getMsg("VAS_Top5") : VIS.Msg.getMsg("VAS_TopPurchase5")) + '</div>');
            YearBasedDataListDiv = $('<div class="VAS-YearBasedDataListDiv">');
            $YearBasedDataListDiv = $('<div class="input-group vis-input-wrap">');
            /* parameters are: context, windowno., coloumn id, display type, DB coloumn name, Reference key, Is parent, Validation Code*/
            $YearBasedDataListLookUp = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.List, "VAS_YearBasedData", ColumnIds.AD_Reference_ID, false, null);
            // Parameters are: columnName, mandatory, isReadOnly, isUpdateable, lookup,display length
            $self.vYearBasedDataList = new VIS.Controls.VComboBox("VAS_YearBasedData", true, false, true, $YearBasedDataListLookUp, 20);
            $self.vYearBasedDataList.setValue("CM");
            var $YearBasedDataListControlWrap = $('<div class="vis-control-wrap">');
            $YearBasedDataListDiv.append($YearBasedDataListControlWrap);
            $YearBasedDataListControlWrap.append($self.vYearBasedDataList.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' '));
            $YearBasedDataListDiv.append($YearBasedDataListControlWrap);
            YearBasedDataListDiv.append($YearBasedDataListDiv);
            HeadingComboDiv.append(HeadingDiv).append(YearBasedDataListDiv);
            $maindiv.append(HeadingComboDiv);
            $self.vYearBasedDataList.fireValueChanged = function () {
                $self.vYearBasedDataList.setValue($self.vYearBasedDataList.getValue());
                $maindiv.find('#vas_listContainer_' + widgetID).remove();
                $maindiv.find('#vas_norecordcont_' + widgetID).remove();
                $bsyDiv[0].style.visibility = "visible";
                $self.intialLoad();
            }
            $bsyDiv[0].style.visibility = "visible";
        };
        function createDummyDiv() {
            $dummDiv = '<div class="vas-igtwidg-invoices-box vas-idtwidg-emptydiv">'+
                '<div class="vas-igtwidg-invoices-detail" >'+
                '<div class="vas-igtwidg-thumb-w-txt">'+
                '<img src="" alt="" class="vas-igtwidg-emptyimg">'+
                '<div class="vas-igtwidg-vendor-w-date">'+
                 '<div class="vas-igtwidg-vendor-name"></div>'+
                '<div class="vas-igtwidg-invoiceDate"></div>'+
               '</div>'
               '</div>'+
               '<div class="vas-igtwidg-invoiceTotalAmt"><span class="vas-igtwidg-amt-val"></span><span class="vas-igtwidg-cur-symbol"><span></span></span></div>'+
               '</div>'+
               '</div>';
        };
        function bindDummyDiv(j) {
            for (var i = 0; i < j; i++) {
                listDesign.append($dummDiv);
            };
        };

        /*This function will load data in widget */
        this.intialLoad = function () {
            var hue = Math.floor(Math.random() * (360 - 0)) + 0;
            var v = Math.floor(Math.random() * (75 - 60 + 1)) + 60;
            var pastel = 'hsl(' + hue + ', 100%,' + v + '%)';
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetInvTotalGrandData", { "ISOtrx": VIS.Env.getCtx().isSOTrx($self.windowNo), "ListValue": $self.vYearBasedDataList.getValue()}, function (dr) {
                var gridDataResult = dr;
                if (gridDataResult != null && gridDataResult.length > 0) {
                    InitailizeMessage();
                    // Create the container for the list
                     listDesign = $('<div class="vas-igtwidg-vendors-listing" id="vas_listContainer_' + widgetID + '">');

                    // Iterate through each item in the gridDataResult
                    for (var i = 0; i < gridDataResult.length; i++) {
                        RecCount+=1
                        var custChar = '';
                        var custNameArr = gridDataResult[i].Name.trim().split(' ');

                        // Generate customer initials
                        custChar = custNameArr[0].substring(0, 1).toUpperCase();
                        if (custNameArr.length > 1) {
                            custChar += custNameArr[custNameArr.length - 1].substring(0, 1).toUpperCase();
                        } else {
                            custChar = custNameArr[0].substring(0, 2).toUpperCase();
                        }

                        //convertAmountToDotFormat(formatLargeNumber(gridDataResult[i].GrandTotalAmt, gridDataResult[i].stdPrecision));
                        // Create the widget data design element
                        var widgetDataDesign = '<div class="vas-igtwidg-invoices-box">' +
                            '<div class= "vas-igtwidg-invoices-detail">' +
                            '<div class="vas-igtwidg-thumb-w-txt">'

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
                            '<div class="vas-igtwidg-vendor-w-date">' +
                            '<div class="vas-igtwidg-vendor-name">' + gridDataResult[i].Name + '</div>' +
                            //'<div class="vas-igtwidg-invoiceDate">' + VAS.translatedTexts.VAS_Since + ' ' + VIS.Utility.Util.getValueOfDate(gridDataResult[i].SinceDate).toLocaleDateString() + '</div>' +
                            '</div>' +
                            '</div >' 
                        //if (unit != null || unit == undefined) {
                            widgetDataDesign += '<div class="vas-igtwidg-invoiceTotalAmt"><span class="vas-igtwidg-amt-val">' + (gridDataResult[i].Symbol.length != 3 ? '<span class="vas-vaswidg-Symbol">' + gridDataResult[i].Symbol + '</span>' : '')
                                + formatLargeNumber(gridDataResult[i].GrandTotalAmt, gridDataResult[i].stdPrecision) +'<span style="font-weight: 600;padding-left:1px;">' + unit + '</span>'
                               '<span>'+ (gridDataResult[i].Symbol.length == 3 ? ' ' + gridDataResult[i].Symbol : '') +'</span>';
                            //widgetDataDesign += '<div class="vas-igtwiginvmillion">' + TotalAmtArray[1] + '<span style="font-weight: 600;padding-left:1px;">' + unit + '</span>'
                            //    + (gridDataResult[i].Symbol.length == 3 ? ' ' + gridDataResult[i].Symbol : '') + '</div>'
                        //}
                        //else {
                        //    widgetDataDesign += '<span class="vas-igtwidg-cur-symbol"> ' + TotalAmtArray[1] + (gridDataResult[i].Symbol.length == 3 ? '<span>' + gridDataResult[i].Symbol + '</span>':'') +'</span></div> '
                        //}
                        widgetDataDesign += '</div >' +
                            '</div >'

                        // Append the widget data design to the list container
                        listDesign.append(widgetDataDesign);
                    }
                    if (RecCount < ExpectedRecordCount) {
                        bindDummyDiv(ExpectedRecordCount - RecCount);
                    }
                    $maindiv.append(listDesign);
                    $root.append($maindiv);
                }
                else {
                    $maindiv.append('<div class="vas-igwidg-notfounddiv" id="vas_norecordcont_' + widgetID + '">' + VIS.Msg.getMsg("VAS_RecordNotFound") + '</div>')
                    $root.append($maindiv);
                }
                $bsyDiv[0].style.visibility = "hidden";
            });
        };
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
        function InitailizeMessage() {
            /*Inisalised Element for array*/
            var elements = [
                "VAS_Since",
                "VAS_Top5",
                "VAS_Million",
                "VAS_Trillion",
                "VAS_Billion",
                "VAS_Thousand",
                "VAS_TopPurchase5"
            ];

            VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);

        }
        /**
         * This Function is responsible converting the value into million
         * @param {any} number
         * @param {any} stdPrecision
         */
        function formatLargeNumber(number, stdPrecision) {
            if (number >= 1000000000000) { /* Trillion*/
                unit = VAS.translatedTexts.VAS_Trillion;
                return (number / 1000000000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else if (number >= 1000000000) { /* Billion*/
                unit = VAS.translatedTexts.VAS_Billion;;
                return (number / 1000000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else if (number >= 1000000) { /* Million*/
                unit = VAS.translatedTexts.VAS_Million;
                return (number / 1000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
            else if (number >= 1000) { /* Thousand*/
                unit = VAS.translatedTexts.VAS_Thousand;
                return (number / 1000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
            else {
                unit = '';
                return (number).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            }
        }
        /**
       * This function is used to get the refernce id of list
       */
        var GetColumnID = function () {
            ColumnIds = VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetColumnID", { "refernceName": "VAS_YearBasedData" }, null);
        }
        /**
        * This Function is responsible for dividing total amount in array
        * @param {any} AmtVal
        */
        //function convertAmountToDotFormat(AmtVal) {
        //    //Get decimal seperator
        //    var isDotSeparator = culture.isDecimalSeparatorDot(window.navigator.language);

        //    if (!isDotSeparator) {
        //        if (AmtVal.contains(",")) {
        //            TotalAmtArray = AmtVal.split(",")
        //            TotalAmtArray[2] = ",";
        //        }
        //    }
        //    else {
        //        TotalAmtArray = AmtVal.split(".")
        //        TotalAmtArray[2] = ".";
        //    }
        //}
        /*This function used to get root*/
        this.getRoot = function () {
            return $root;
        };
        /*this function is used to refresh design and data of widget*/
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            $maindiv.find('#vas_listContainer_' + widgetID).remove();
            $maindiv.find('#vas_norecordcont_' + widgetID).remove();
            $self.intialLoad();
        };
    };

    VAS.VAS_InvGrandTotalWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_InvGrandTotalWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };
    /*this function is called when widget size changes*/
    VAS.VAS_InvGrandTotalWidget.prototype.widgetSizeChange = function (widget) {
        //this.getRoot().css("height", widget.height);
        //this.getRoot().css("width", widget.width);
        this.widgetInfo = widget;
    };
    /*Used to dispose the variable*/
    VAS.VAS_InvGrandTotalWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);