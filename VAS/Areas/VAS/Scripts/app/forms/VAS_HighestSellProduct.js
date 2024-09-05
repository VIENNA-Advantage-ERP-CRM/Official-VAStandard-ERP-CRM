/************************************************************
 * Module Name    : VAS
 * Purpose        : Created Widget To get Highest selling data of top 10 Product
 * chronological  : Development
 * Created Date   : 28 August 2024
 * Created by     : VIS0060
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_HighestSellProduct = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100">');
        var $divProduct = "", $divCurrentRevenue = "", $divLastRevenue = "";
        var TopProductData = [];
        var culture = new VIS.CultureSeparator();
        var unit = null, spnPageCount = null;
        var prevPage = null;
        var nextPage = null;
        var widgetID = 0;
        var count = 0;

        /*Intialize function will intialize busy indiactor*/
        this.initalize = function () {
            createBusyIndicator();
            widgetID = this.widgetInfo.AD_UserHomeWidgetID;
            $root.append('<div class="VAS-selling-container VAS-highest-selling-prod">' +
                '<div class="VAS-container-heading">' + VIS.Msg.getMsg("VAS_HighestSellProduct") + '</div>' +
                '<div class="VAS-org-col" id="VAS_divProduct_' + widgetID + '">' +
                '</div>' +
                '<div class="VAS-yearly-revenue">' +
                '<div class="VAS-lastRevenue" id="VAS_divLastYear_' + widgetID + '">' +
                '</div>' +
                '<div class="VAS-currentRevenue" id="VAS_divCurrentYear_' + widgetID + '">' +
                '</div>' +
                '<div class="VAS-pagination-col">' +
                '<div class= "VAS-arrow-col">' +
                '<a href="javascript:void(0);"><span class="fa fa-arrow-left" id="VAS_Prev_' + widgetID + '"></span></a>' +
                '<a href="javascript:void(0);"><span class="fa fa-arrow-right" id="VAS_Next_' + widgetID + '"></span></a>' +
                '</div>' +
                '<div class="VAS-page-count" id="VAS_spnPageCount_' + widgetID + '">1 of 10</div>' +
                '</div>' +
                '</div>' +
                '</div>');

            $divProduct = $root.find("#VAS_divProduct_" + widgetID);
            $divLastRevenue = $root.find("#VAS_divLastYear_" + widgetID);
            $divCurrentRevenue = $root.find("#VAS_divCurrentYear_" + widgetID);
            spnPageCount = $root.find("#VAS_spnPageCount_" + widgetID);
            prevPage = $root.find("#VAS_Prev_" + widgetID);
            nextPage = $root.find("#VAS_Next_" + widgetID);

            prevPage.on(VIS.Events.onTouchStartOrClick, function () {
                if (count > 0) {
                    $bsyDiv[0].style.visibility = "visible";
                    count--;
                    $divProduct.empty();
                    $divLastRevenue.empty();
                    $divCurrentRevenue.empty();
                    setProductData();
                }
            });

            nextPage.on(VIS.Events.onTouchStartOrClick, function () {
                if (count < TopProductData.length - 1) {
                    $bsyDiv[0].style.visibility = "visible";
                    count++;
                    $divProduct.empty();
                    $divLastRevenue.empty();
                    $divCurrentRevenue.empty();
                    setProductData();
                }
            });
        };

        /*This function will load data in widget */
        this.intialLoad = function () {
            $divProduct.empty();
            $divLastRevenue.empty();
            $divCurrentRevenue.empty();
            count = 0;
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "Product/GetTopProductData", "", function (dr) {
                if (dr != null) {
                    TopProductData = dr;
                    setProductData();
                }
                $bsyDiv[0].style.visibility = "hidden";
            });
        };

        function setProductData() {
            $divProduct.append('<div class="VAS-orgName-col">' +
                '<h1>#' + (count + 1) + '</h1>' +
                '<div class="VAS-org-name"><span>' + TopProductData[count].Name + '</span></div>' +
                '</div>' +
                '<img src="' + (TopProductData[count].ImageUrl != "" ? VIS.Application.contextUrl + TopProductData[count].ImageUrl :
                    VIS.Application.contextUrl + 'Areas/VAS/Content/Images/selling-img.png') + '" alt="">');

            $divLastRevenue.append('<h1><span style="margin-right:0.2em;">' + TopProductData[count].Symbol + '</span>' + formatLargeNumber(TopProductData[count].PreviousTotal, TopProductData[count].StdPrecision) +
                ((unit != null) ? '<span>' + unit + '</span>' : '') + '<span class="VAS-sale-Qty">(' + TopProductData[count].PreviousQty + ' ' +
                TopProductData[count].UOM + ')</span>' +
                '</h1><span>' + VIS.Msg.getMsg("VAS_LastYear") + '</span>');

            $divCurrentRevenue.append('<h1><span style="margin-right:0.2em;">' + TopProductData[count].Symbol + '</span>' + formatLargeNumber(TopProductData[count].CurrentTotal, TopProductData[count].StdPrecision) +
                ((unit != null) ? '<span>' + unit + '</span>' : '') + '<span class="VAS-sale-Qty"">(' + TopProductData[count].CurrentQty + ' ' +
                TopProductData[count].UOM + ')</span>' +
                '</h1><span>' + VIS.Msg.getMsg("VAS_CurrentYear") + '</span>');

            if (count > 0) {
                prevPage.css("opacity", "1");
            }
            else {
                prevPage.css("opacity", "0.6");
            }

            if (TopProductData.length - 1 > count) {
                nextPage.css("opacity", "1");
            }
            else {
                nextPage.css("opacity", "0.6");
            }
            spnPageCount.text((count + 1) + VIS.Msg.getMsg("VAS_Of") + TopProductData.length);
            $bsyDiv[0].style.visibility = "hidden";
        }

        /**
         * This Function is responsible converting the value into million
         * @param {any} number
         * @param {any} stdPrecision
         */
        function formatLargeNumber(number, stdPrecision) {
            if (number >= 1000000000000) { /* Trillion*/
                unit = VIS.Msg.getMsg("VAS_Trillion");
                return (number / 1000000000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else if (number >= 1000000000) { /* Billion*/
                unit = VIS.Msg.getMsg("VAS_Billion");
                return (number / 1000000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else if (number >= 1000000) { /* Million*/
                unit = VIS.Msg.getMsg("VAS_Million");
                return (number / 1000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
            else if (number >= 1000) { /* Thousand*/
                unit = VIS.Msg.getMsg("VAS_Thousand");
                return (number / 1000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
            else {
                unit = null;
                return (number).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            }
        }

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
                    TotalAmtArray[2] = ",";
                }
            }
            else {
                TotalAmtArray = AmtVal.split(".")
                TotalAmtArray[2] = ".";
            }
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

        /*This function used to get root*/
        this.getRoot = function () {
            return $root;
        };

        /*this function is used to refresh design and data of widget*/
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            count = 0;
            $self.intialLoad();
        };
    };

    VAS.VAS_HighestSellProduct.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_HighestSellProduct.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /*this function is called when widget size changes*/
    VAS.VAS_HighestSellProduct.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    /*Used to dispose the variable*/
    VAS.VAS_HighestSellProduct.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);