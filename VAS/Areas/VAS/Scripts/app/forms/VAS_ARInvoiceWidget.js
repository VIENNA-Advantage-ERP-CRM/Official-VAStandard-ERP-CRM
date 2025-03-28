﻿/************************************************************
 * Module Name    : VAS
 * Purpose        : Created Widget To get AR Invoice Data
 * chronological  : Development
 * Created Date   : 21 August 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_ARInvoiceWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100">');
        var $maindiv = $('<div class="vas-arwidg-receivable-container">');
        var ctx = this.ctx;
        var TotalAmtArray = [];
        var culture = new VIS.CultureSeparator();
        var msgArray = null;
        var widgetID = null;
        var unit = null;
        /*Intialize function will intialize busy indiactor*/
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);
            createBusyIndicator();
            $bsyDiv[0].style.visibility = "visible";
        };

        /*This function will load data in widget */
        this.intialLoad = function () {
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetARInvSchData", { "ISOtrx": VIS.Env.getCtx().isSOTrx($self.windowNo) }, function (dr) {
                var gridDataResult = dr;
                if (gridDataResult != null && gridDataResult.length > 0) {
                    /* This function used to intialize the image*/
                    InitailizeMessage();
                    //Seprating the value
                    convertAmountToDotFormat((gridDataResult[5].arTotalAmtWidget[0].totalAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: gridDataResult[0].stdPrecision, maximumFractionDigits: gridDataResult[0].stdPrecision }))
                    $maindiv.append(
                        '  <div class="vas-arwidg-totalAmt-box" id="vas_arwidtotaamtContainer_' + widgetID + '">' +
                        '    <div class="vas-arwidg-totalRec-amount">' +
                        '      <h1 class="vas-arwidg-rtl-amt">' + TotalAmtArray[0] +
                        '<span class="vas-arwidg-cur-symbol">' + TotalAmtArray[1] + '<span>' +
                        '<span class="vas-arwidg-Symbol">' + gridDataResult[4].Symbol + '</span></h1>' +
                        '      <div class="vas-arwidg-totalRecTxt">' + (VIS.Env.getCtx().isSOTrx($self.windowNo) == true ? VAS.translatedTexts.VAS_TotalRec : VAS.translatedTexts.VAS_TotalPurchase) + '</div>' +
                        '    </div>' +
                        '  </div>');
                    var listDesign = $('<div class="vas-arwidg-rec-listing" id="vas_listContainer_' + widgetID + '">');
                    for (var i = 0; i < gridDataResult.length - 1; i++) {
                        var widgetDataDesign = '<div class="vas-arwidg-receiveTxt-box">' +
                            '<div class="vas-arwidg-orderTxt">' + msgArray[i] + '</div>' +
                            '<div class="vas-arwidg-recBox-amt">' + '<span class="vas-vaswidg-Symbol">' + gridDataResult[i].Symbol + '</span>' + formatLargeNumber(gridDataResult[i].daysAmt, gridDataResult[i].stdPrecision) + unit
                            '</div>' +
                            '</div>'
                        listDesign.append(widgetDataDesign);
                    }
                    $maindiv.append(listDesign);

                    $root.append($maindiv);
                }
                $bsyDiv[0].style.visibility = "hidden";
            });
        };
        /**
        * This Function is responsible converting the value into million
        * @param {any} number
        * @param {any} stdPrecision
        */
        function formatLargeNumber(number, stdPrecision) {
            // Determine the sign of the number
            var isNegative = number < 0;
            // Work with the absolute value for formatting
            var absNumber = Math.abs(number);

            var formattedNumber;

            // Determine the unit and format the number accordingly
            if (absNumber >= 1000000000000) { /* Trillion */
                unit = VAS.translatedTexts.VAS_Trillion;
                formattedNumber = (absNumber / 1000000000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else if (absNumber >= 1000000000) { /* Billion */
                unit = VAS.translatedTexts.VAS_Billion;
                formattedNumber = (absNumber / 1000000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
            else {
                unit = '';
                formattedNumber = absNumber.toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            }
            // Return the formatted number with the correct sign and unit
            return (isNegative ? '-' : '') + formattedNumber;
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
        * This Function is responsible for dividing total amount in array
        * @param {any} AmtVal
        */
        function convertAmountToDotFormat(AmtVal) {
            //Get decimal seperator
            var isDotSeparator = culture.isDecimalSeparatorDot(window.navigator.language);

            if (!isDotSeparator) {
                if (AmtVal.contains(",")) {
                    TotalAmtArray = AmtVal.split(",")
                    TotalAmtArray[3] = ",";
                }
            }
            else {
                TotalAmtArray = AmtVal.split(".")
                TotalAmtArray[3] = ".";
            }
            //If decimal separator is zero then not to consider its value
            if (VIS.Utility.Util.getValueOfDecimal(TotalAmtArray[1]) == 0) {
                TotalAmtArray[1] = "";
            }
        }
        /*This function used translate message*/
        function InitailizeMessage() {
            /*Inisalised Element for array*/
            var elements = [
                "VAS_30",
                "VAS_30to60",
                "VAS_60to90",
                "VAS_90to120",
                "VAS_Older",
                "VAS_TotalRec",
                "VAS_TotalPurchase",
                "VAS_Trillion",
                "VAS_Billion"
            ];

            VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);
            msgArray = [VAS.translatedTexts.VAS_30, VAS.translatedTexts.VAS_30to60, VAS.translatedTexts.VAS_60to90, VAS.translatedTexts.VAS_90to120, VAS.translatedTexts.VAS_Older];

        }
        /*This function used to get root*/
        this.getRoot = function () {
            return $root;
        };
        /*this function is used to refresh design and data of widget*/
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            $maindiv.find('#vas_arwidtotaamtContainer_' + widgetID).remove();
            $maindiv.find('#vas_listContainer_' + widgetID).remove();
            $self.intialLoad();
        };
    };

    VAS.VAS_ARInvoiceWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_ARInvoiceWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };
    /*this function is called when widget size changes*/
    VAS.VAS_ARInvoiceWidget.prototype.widgetSizeChange = function (widget) {
        //this.getRoot().css("height", widget.height);
        //this.getRoot().css("width", widget.width);
        this.widgetInfo = widget;
    };
    /*Used to dispose the variable*/
    VAS.VAS_ARInvoiceWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);