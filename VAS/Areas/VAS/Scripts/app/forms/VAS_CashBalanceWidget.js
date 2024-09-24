/************************************************************
 * Module Name    : VAS
 * Purpose        : Cash Balance Widget
 * chronological  : Development
 * Created Date   : 23 Sep 2024
 * Created by     : VIS103
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {
    VAS.VAS_CashBalanceWidget = function () {
        this.frame;
        this.windowNo;
        var $self = this;
        var $root;
        var $bsyDiv;
        var $divCashDet;
        var $divCashBody;
        var CurrentPage = 0;
        var pageSize = 4;
        var cashBalData = [];
        var $prevArrow;
        var $pageInfo;
        var widgetID = null;
        var $divBusy;

        /*This function will load data in widget */
        function initializeComponent() {
            getControls();
            events();
        };
        /**Get data from server for upcoming  */
        function GetCashBalanceData() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_PaymentWidget/GetCashBookBalance",
                dataType: "json",
                async: true,
                success: function (data) {
                    var divString = "";
                    cashBalData = jQuery.parseJSON(data);
                    if (cashBalData == null || cashBalData.length <= 0) {
                        divString += '<div class="VAS_noData">' + VIS.Msg.getMsg("VAS_NoData") + '</div>';
                        $root.append(divString);
                    }
                    else {
                        fillCashBalanceData();
                    }

                    ShowBusy(false);
                }
                ,
                error: function (eror) {
                    console.log(eror);
                    ShowBusy(false);
                }
            });
        };
        function bindDummyDiv(j) {
            for (var i = 0; i < j; i++) {
                $divCashBody.append($divCashDet);
            };
        }
        /**Bind Cashbook data to container */
        function fillCashBalanceData() {
            $divCashBody.empty();
            if (cashBalData != null) {
                var start = CurrentPage * pageSize;
                var end = Math.min(start + pageSize, cashBalData.length);
                var TxtPageFooter = (CurrentPage + 1).toString() +  " " + VIS.Msg.getMsg("VAS_Of") + " "  + Math.ceil(cashBalData.length / pageSize).toString();
                $pageInfo.text(TxtPageFooter);
                for (var i = start; i < end; i++) {
                    $divCashBody.append('<div class="VAS-cashDetail-box" id="div_cashDetail_' + widgetID + '">'
                        + '<div class="VAS-cashbook-name">' + cashBalData[i].Name + '</div>'
                        + '<div class="VAS-CashBaldata"><div class="VAS-cashISOCode"> ' + cashBalData[i].ISO_Code + '</div> <div class="VAS-cashbook-amount">'
                        + cashBalData[i].CompletedBalance.toLocaleString(window.navigator.language,
                            { minimumFractionDigits: cashBalData[i].StdPrecision, maximumFractionDigits: cashBalData[i].StdPrecision }) + '</div></div>'
                        + '</div>');
                };
                if (end - start < pageSize) {
                    bindDummyDiv(pageSize - (end - start));
                }
                if (CurrentPage == 0) {
                    $prevArrow.addClass("disabled");
                    $nextArrow.removeClass("disabled");
                }
                else {
                    $prevArrow.removeClass("disabled");
                    if (end == cashBalData.length) {
                        $nextArrow.addClass("disabled");
                    }
                    else {
                        $nextArrow.removeClass("disabled");
                    }
                }
                ShowBusy(false);
            }
        };

        /** bind events for arrow buttons */
        function events() {
            $nextArrow.on("click", function (e) {
                ShowBusy(true);
                if ((CurrentPage + 1) * pageSize < cashBalData.length) {
                    CurrentPage++;
                    fillCashBalanceData();
                }
                ShowBusy(false);
            })

            $prevArrow.on("click", function (e) {
                ShowBusy(true);
                if (CurrentPage > 0) {
                    CurrentPage--;
                    fillCashBalanceData();
                }
                ShowBusy(false);
            })
        };
        /** Get control elements from root */
        function getControls() {
            $divBankBal = $root.find("#div_cashBal_widget_" + widgetID);
            $divCashBody = $root.find("#VAS_divCashBody_" + widgetID);
            $divCashDet = $root.find("#div_cashDetail_" + widgetID);
            $nextArrow = $root.find("#VAS_CashNextArrow_" + widgetID);
            $prevArrow = $root.find("#VAS_CashPrevArrow_" + widgetID);
            $pageInfo = $root.find("#VAS_PageInfo_" + widgetID);
            $divBusy = $root.find("#busyDivId_" + widgetID);
            ShowBusy(true);
            GetCashBalanceData();
        }

        /*Create Busy Indicator */
        function createBusyIndicator() {
            $bsyDiv = $('<div id="busyDivId_' + widgetID + '" class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($bsyDiv);
        };
        /* Method to enable and disable busy indicator */
        function ShowBusy(show) {
            if (show) {
                $divBusy.show();
            }
            else {
                $divBusy.hide();
            }
        };
        /*this function is used to refresh design and data of widget*/
        this.refreshWidget = function () {
            ShowBusy(true);
            initializeComponent();
        };
        /*This function is used to intialize the design structure */
        this.Initialize = function () {
            widgetID = this.widgetInfo.AD_UserHomeWidgetID;
            if (widgetID == 0) {
                widgetID = $self.windowNo;
            }
            $root = $('<div class="VAS-cashroot"  id="VAS_cashroot_"></div>');
            createBusyIndicator();
            $root.append('<div class="VAS-CashBal-container"  id="div_cashBal_widget_' + widgetID + '"><div class="VAS-cashbalance-container" id="div_bankBal_' + widgetID + '">' +
                '<div class="VAS-cashwidget-header">' +
                '<h1>' + VIS.Msg.getMsg("VAS_CashBalance") + '</h1>' +
                '<div class="VAS-casharrow-control">' +
                '<a href="#"><i id="VAS_CashPrevArrow_' + widgetID + '" class="fa fa-arrow-circle-left" aria-hidden="true"></i></a>' +
                '<span id="VAS_PageInfo_' + widgetID + '" ></span>' +
                '<a href="#"><i id="VAS_CashNextArrow_' + widgetID + '" class="fa fa-arrow-circle-right" aria-hidden="true"></i></a>' +
                '</div>' +
                '</div><div class="VAS-cashwidget-body" id="VAS_divCashBody_' + widgetID + '"></div></div>');
            initializeComponent();
        };
        //Privilized function
        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_CashBalanceWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_CashBalanceWidget.prototype.init = function (windowNo, frame) {
        //Assign to this Varable
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        var obj = this.Initialize();
        this.frame.getContentGrid().append(this.getRoot());

    };
    VAS.VAS_CashBalanceWidget.prototype.dispose = function () {
        /*CleanUp Code */
        //dispose this component
        //this.disposeComponent();
        //call frame dispose function
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };

})(VAS, jQuery);