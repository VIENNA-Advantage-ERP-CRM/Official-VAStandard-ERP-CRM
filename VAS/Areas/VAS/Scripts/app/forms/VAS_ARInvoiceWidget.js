/************************************************************
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
        /*Inisalised Element for array*/
        var elements = [
            "VAS_30",
            "VAS_30to60",
            "VAS_60to90",
            "VAS_90to120",
            "VAS_Older",
            "VAS_TotalRec"
        ];

        VAS.translatedTexts = VIS.Msg.translate(ctx, elements, true);
        /*Intialize function will intialize busy indiactor*/
        this.initalize = function () {
            createBusyIndicator();
            $bsyDiv[0].style.visibility = "visible";
        };

        var msgArray = [VAS.translatedTexts.VAS_30, VAS.translatedTexts.VAS_30to60, VAS.translatedTexts.VAS_60to90, VAS.translatedTexts.VAS_90to120, VAS.translatedTexts.VAS_Older];

        /*This function will load data in widget */
        this.intialLoad = function () {
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetARInvSchData", "", function (dr) {
                var gridDataResult = dr;
                if (gridDataResult != null) {
                    //Seprating the value
                    convertAmountToDotFormat((gridDataResult[5].arTotalAmtWidget[0].totalAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: gridDataResult[0].stdPrecision, maximumFractionDigits: gridDataResult[0].stdPrecision }))
                    $maindiv.append(
                        '  <div class="vas-arwidg-totalAmt-box" id="vas_arwidtotaamtContainer_' + $self.windowNo + '">' +
                        '    <div class="vas-arwidg-totalRec-amount">' +
                        '      <h1>' + TotalAmtArray[0] +
                        '<span class="vas-arwidg-cur-symbol">' + TotalAmtArray[3] + '' + TotalAmtArray[1] + '<span>' +
                        '<span>' + gridDataResult[4].Symbol + '</span></h1>' +
                        '      <div class="vas-arwidg-totalRecTxt">' + VAS.translatedTexts.VAS_TotalRec + '</div>' +
                        '    </div>' +
                        '  </div>');
                    var listDesign = $('<div class="vas-arwidg-rec-listing" id="vas_listContainer_' + $self.windowNo + '">');
                    for (var i = 0; i < gridDataResult.length - 1; i++) {
                        var widgetDataDesign = '<div class="vas-arwidg-receiveTxt-box">' +
                            '<div class="vas-arwidg-orderTxt">' + msgArray[i] + '</div>' +
                            '<div class="vas-arwidg-recBox-amt">' + (gridDataResult[i].daysAmt).toLocaleString(window.navigator.language, { minimumFractionDigits: gridDataResult[i].stdPrecision, maximumFractionDigits: gridDataResult[i].stdPrecision }) +
                            '<span>' + gridDataResult[i].Symbol + '</span></div>' +
                            '</div>'
                        listDesign.append(widgetDataDesign);
                    }
                    $maindiv.append(listDesign);

                    $root.append($maindiv);
                }
                $bsyDiv[0].style.visibility = "hidden";
            });
        };
        /*This function will show busy indicator in widget */
        function createBusyIndicator() {
            $bsyDiv = $("<div class='vis-apanel-busy'>");
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

            //format should not be dot format
            if (AmtVal != '' && !isDotSeparator) {
                if (!AmtVal.contains("=")) {
                    if (AmtVal.contains(",")) {
                        //replace , with .
                        TotalAmtArray = AmtVal.split(",")
                        TotalAmtArray[3] = ",";
                    }
                }
            }
            else {
                TotalAmtArray = AmtVal.split(".")
                TotalAmtArray[3] = ".";
            }
        }
        /*This function used to get root*/
        this.getRoot = function () {
            return $root;
        };
        /*this function is used to refresh design and data of widget*/
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            $maindiv.find('#vas_arwidtotaamtContainer_' + $self.windowNo).remove();
            $maindiv.find('#vas_listContainer_' + $self.windowNo).remove();
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