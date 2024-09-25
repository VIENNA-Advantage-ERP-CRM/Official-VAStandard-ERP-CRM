/************************************************************
 * Module Name    : VAS
 * Purpose        : Bank Balance Widget
 * chronological  : Development
 * Created Date   : 19 Sep 2024
 * Created by     : VIS103
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {
    VAS.VAS_BankBalanceWidget = function () {
        this.frame;
        this.windowNo;
        var $self = this;
        var $root;
        var $bsyDiv;
        var $divBankBal;
        var $divBankDet;
        var $divBankBody;
        var CurrentPage = 0;
        var pageSize = 4;
        var bankBalData = [];
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
        function GetBankBalanceData() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_PaymentWidget/GetBankBalance",
                dataType: "json",
                async: true,
                success: function (data) {
                    var divString = "";
                    bankBalData = jQuery.parseJSON(data);
                    if (bankBalData == null || bankBalData.length <= 0) {
                        divString += '<div class="VAS_noData">' + VIS.Msg.getMsg("VAS_NoData") + '</div>';
                        $root.append(divString);
                    }
                    else {
                        fillBankBalnaceData();
                    }
                    ShowBusy(false);
                },
                error: function (eror) {
                    console.log(eror);
                    ShowBusy(false);
                }
            });
        };
        function bindDummyDiv(j) {
            for (var i = 0; i < j; i++) {
                $divBankBody.append($divBankDet);
            };
        }
        /**Bind bank balance data to container */
        function fillBankBalnaceData() {
            $divBankBody.empty();
            if (bankBalData != null) {
                var start = CurrentPage * pageSize;
                var end = Math.min(start + pageSize, bankBalData.length);
                var TxtPageFooter = (CurrentPage + 1).toString() + " "+ VIS.Msg.getMsg("VAS_Of") + " "+ Math.ceil(bankBalData.length / pageSize).toString();
                $pageInfo.text(TxtPageFooter);
                for (var i = start; i < end; i++) {
                    $divBankBody.append('<div class="VAS-bankBalDetail-box" id="div_bankDetail_' + widgetID + '">'
                        + '<div class="VAS-bankbal-name">' + bankBalData[i].Name + ' - ' + bankBalData[i].AccountNo + '</div>'
                        + '<div class="VAS-BankBaldata"><div class="VAS-bankbalISOCode"> ' + bankBalData[i].ISO_Code + '</div> <div class="VAS-totalbankbal-amount">'
                        + bankBalData[i].EndingBalance.toLocaleString(window.navigator.language,
                            { minimumFractionDigits: bankBalData[i].StdPrecision, maximumFractionDigits: bankBalData[i].StdPrecision }) + '</div></div>'
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
                    if (end == bankBalData.length) {
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
                if ((CurrentPage + 1) * pageSize < bankBalData.length) {
                    CurrentPage++;
                    fillBankBalnaceData();
                }
                ShowBusy(false);
            })

            $prevArrow.on("click", function (e) {
                ShowBusy(true);
                if (CurrentPage > 0) {
                    CurrentPage--;
                    fillBankBalnaceData();
                }
                ShowBusy(false);
            })
        };
        /** Get control elements from root */
        function getControls() {
            $divBankBal = $root.find("#div_bankBal_widget_" + widgetID);
            $divBankBody = $root.find("#VAS_divbankBody_" + widgetID);
            $divBankDet = $root.find("#div_bankDetail_" + widgetID);
            $nextArrow = $root.find("#VAS_BankBalNextArrow_" + widgetID);
            $prevArrow = $root.find("#VAS_BankBalPrevArrow_" + widgetID);
            $pageInfo = $root.find("#VAS_PageInfo_" + widgetID);
            $divBusy = $root.find("#busyDivId_" + widgetID);
            ShowBusy(true);
            GetBankBalanceData();
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
            $root = $('<div class="VAS-bankBalroot" id="VAS_root_"></div>');
            $root.append('<div class="VAS-BankBal-container"  id="div_bankBal_widget_' + widgetID + '"><div class="VAS-bankbalance-container" id="div_bankBal_' + widgetID + '">' +
                '<div class="VAS-bankbalwidget-header">' +
                '<h1>' + VIS.Msg.getMsg("VAS_BankBalance") + '</h1>' +
                '<div class="VAS-bankBalarrow-control">' +
                '<a href="#"><i id="VAS_BankBalPrevArrow_' + widgetID + '" class="fa fa-arrow-circle-left" aria-hidden="true"></i></a>' +
                '<span id="VAS_PageInfo_' + widgetID + '" ></span>' +
                '<a href="#"><i id="VAS_BankBalNextArrow_' + widgetID + '" class="fa fa-arrow-circle-right" aria-hidden="true"></i></a>' +
                '</div>' +
                '</div><div class="VAS-bankbalwidget-body" id="VAS_divbankBody_' + widgetID + '"></div></div>');
            createBusyIndicator();
            initializeComponent();
        };
        //Privilized function
        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_BankBalanceWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_BankBalanceWidget.prototype.init = function (windowNo, frame) {
        //Assign to this Variable
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        var obj = this.Initialize();
        this.frame.getContentGrid().append(this.getRoot());

    };
    VAS.VAS_BankBalanceWidget.prototype.dispose = function () {
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
        $divBusy = null;
    };

})(VAS, jQuery);