/************************************************************
 * Module Name    : VAS
 * Purpose        : Created Widget To Sale/Purchase state
 * chronological  : Development
 * Created Date   : 28 August 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_PurchaseStateDetailWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100">');
        var $maindiv = $('<div class="vas-psdwidg-purchase-state-col">');
        var ctx = VIS.Env.getCtx();
        var unit = null;
        var msgArray = null;

        /*Intialize function will intialize busy indiactor*/
        this.initalize = function () {
            createBusyIndicator();
            $bsyDiv[0].style.visibility = "visible";
        };
       
        /*Looping through this array to show icon of amount*/
        var icon = ['<i class="fa fa-hourglass-start"></i>', '<i class="fa fa-hourglass-start"></i>', '<i class="fa fa-handshake-o"></i>',
            '<i class="fa fa-times"></i>', '<i class="fa fa-check-square"></i>', '<i class="vis vis-createdocumentx"></i>','<i class="vis vis-drafted"></i>']

        /*This function will load data in widget */
        this.intialLoad = function () {
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetPurchaseStateDetail", { "ISOtrx": VIS.Env.getCtx().isSOTrx($self.windowNo) }, function (dr) {
                var gridDataResult = dr;
                if (gridDataResult != null && gridDataResult.length > 0) {
                    InitailizeMessage();
                    var HeadingDesign = '<div class="vas-psdwidg-whiteBox vas-psdwidg-tile-heading vas-psdwidg-heading-item">';
                    //If isSotrx tru means sale transaction then will show sale as label else will be purchase
                    if (VIS.Env.getCtx().isSOTrx($self.windowNo)) {
                        HeadingDesign += '<h1><span>' + VAS.translatedTexts.VAS_Sale + '</span><span>' + VAS.translatedTexts.VAS_SaleState + '</span></h1>';
                    }
                    else {
                        HeadingDesign += '<h1 class="vas-psdwidg-stateheading"><span>' + VAS.translatedTexts.VAS_Purchase + '</span><span>' + VAS.translatedTexts.VAS_State + '</span></h1>';
                    }
                    HeadingDesign += '</div>';
                    $maindiv.append(HeadingDesign);
                    $maindiv.append(
                        '<div class="vas-psdwidg-whiteBox vas-psdwidg-itme2">' +
                        '<div class="vas-psdwidg-totalSale">'  + (gridDataResult[0].Symbol.length != 3 ? ' ' + gridDataResult[0].Symbol : '') + ' ' +
                        formatLargeNumber(gridDataResult[0].TotalAmt, gridDataResult[0].stdPrecision) + '<span class="vas-psdwidg-unit">'+unit+'</span>' + (gridDataResult[0].Symbol.length == 3 ? ' ' + gridDataResult[0].Symbol : '')+ '</div>' +
                        '<div class="vas-psdwidg-salesYear">' + icon[0] +' ' + msgArray[0] + '</div>')
                    for (var i = 1; i < gridDataResult.length; i++) {
                        $maindiv.append(
                            '<div class="vas-psdwidg-whiteBox">' +
                            '<div class="vas-psdwidg-totalSale">' + (gridDataResult[i].Symbol.length != 3 ? ' ' + gridDataResult[i].Symbol : '') + ' '
                            + formatLargeNumber(gridDataResult[i].TotalAmt, gridDataResult[i].stdPrecision) + '<span class="vas-psdwidg-unit">' + unit + '</span>' + (gridDataResult[i].Symbol.length == 3 ? ' ' + gridDataResult[i].Symbol : '') + '</div>' +
                            '<div class="vas-psdwidg-salesYear">' + icon[i]+' '  + msgArray[i] + '</div>');
                    }
                    $root.append($maindiv);
                }
                $bsyDiv[0].style.visibility = "hidden";
            });

        };
        /*This function used translate message*/
        function InitailizeMessage() {
            /*Inisalised Element for array*/
            var elements = [
                "VAS_DueAmt",
                "VAS_IPAmt",
                "VAS_Drafted",
                "VAS_IsHold",
                "VAS_DueSoon",
                "VAS_New",
                "VAS_Disputed",
                "VAS_Million",
                "VAS_Trillion",
                "VAS_Billion",
                "VAS_Thousand",
                "VAS_Purchase",
                "VAS_State",
                "VAS_Sale",
                "VAS_SaleState"
            ];

            VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);
            msgArray = [VAS.translatedTexts.VAS_DueAmt, VAS.translatedTexts.VAS_DueSoon, VAS.translatedTexts.VAS_Disputed, VAS.translatedTexts.VAS_IsHold, VAS.translatedTexts.VAS_IPAmt,
            VAS.translatedTexts.VAS_New, VAS.translatedTexts.VAS_Drafted];

        }
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
            } else if (absNumber >= 1000000) { /* Million */
                unit = VAS.translatedTexts.VAS_Million;
                formattedNumber = (absNumber / 1000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else if (absNumber >= 1000) { /* Thousand */
                unit = VAS.translatedTexts.VAS_Thousand;
                formattedNumber = (absNumber / 1000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else {
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

        /*This function used to get root*/
        this.getRoot = function () {
            return $root;
        };
        /*this function is used to refresh design and data of widget*/
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            $maindiv.empty();
            $self.intialLoad();
        };
    };

    VAS.VAS_PurchaseStateDetailWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_PurchaseStateDetailWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };
    /*this function is called when widget size changes*/
    VAS.VAS_PurchaseStateDetailWidget.prototype.widgetSizeChange = function (widget) {
        //this.getRoot().css("height", widget.height);
        //this.getRoot().css("width", widget.width);
        this.widgetInfo = widget;
    };
    /*Used to dispose the variable*/
    VAS.VAS_PurchaseStateDetailWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);