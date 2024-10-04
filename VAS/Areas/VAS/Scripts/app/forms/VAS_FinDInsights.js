/************************************************************
 * Module Name    : VAS
 * Purpose        :Created Widget to get finance data insights
 * chronological  : Development
 * Created Date   : 04 October 2024
 * Created by     : VIS_427
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_FinDInsights = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100">');
        var divContainer = null;
        var widgetID = 0;
        var counter = 0;

        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) != 0 ? this.widgetInfo.AD_UserHomeWidgetID : $self.windowNo);
            createBusyIndicator();
            $root.append('<div class="vas-fdi-feed-container">' +
                '<div class="vas-fdi-spaceBetween">' +
                '<h1 class="vas-fdi-widget-head">' + VIS.Msg.getMsg("VAS_FinDInsights") + '</h1>' +
                '<div class="vas-fdi-marquee" id = "VAS_divContainer_' + widgetID + '">' +
                '<p>' +
                '</p>' +
                '</div>' +
                '</div>');

            divContainer = $root.find("#VAS_divContainer_" + widgetID);
            $root.find(".vas-fdi-refreshIco").hide();
        };

        this.intialLoad = function () {
            divContainer.find('p').empty();
            VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VAS/PoReceipt/GetFinInsightsData", null, function (dr) {
                InsightData = dr;
                if (InsightData.length > 0) {
                    for (i = 0; i < InsightData.length; i++) {
                        divContainer.find('p').append(' <div class="vas-fdi-req-generated">' +
                            '<div class="vas-fdi-req-count"></div>' +
                            '<a href="#" class="vas-fdi-reqGen-Txt vas-custom-link" data-name="' + InsightData[i].Name + '" data-dataobject="' + InsightData[i].DataObject + '" data-tableview="' + InsightData[i].TabelView + '" data-ad_org_id="' + InsightData[i].AD_Org_ID +'">' +
                            '<span style="font-size: 1.8em;">' + InsightData[i].Result + '</span>' + '    ' + '<span style="font-size: 1.2em;">' + InsightData[i].DisplayName + '</span>' +
                            '</a>' +
                            '</div>' +
                            '</div>');
                    }
                }
                else {
                    divContainer.find('p').append('<div class="vas-fdi-notfounddiv" id="vas_norecordcont_' + widgetID + '">' + VIS.Msg.getMsg("VAS_RecordNotFound") + '</div>')
                }

                $bsyDiv[0].style.visibility = "hidden";

                divContainer.find('a').on("click", function () {
                    ++counter;
                    var insightDataView = new VAS.VAS_FinDInsightsGridView();
                    insightDataView.setProperties($(this).attr("data-tableview"), $(this).attr("data-name"), $(this).attr("data-dataobject"), counter, $(this).attr("data-ad_org_id"));
                    insightDataView.Initialize();
                });
            });
        };

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = "visible";
            $root.append($bsyDiv);
        };

        this.getRoot = function () {
            return $root;
        };

        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = "visible";
            $self.intialLoad();
        };
    };

    VAS.VAS_FinDInsights.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_FinDInsights.prototype.widgetSizeChange = function (widget) {
        //size = {
        //    AD_UserHomeWidgetID: 200001,
        //    editMode: true || false,
        //    rows: 2,
        //    Cols: 2,
        //    width: '200px',
        //    height: '200px',
        //}
        this.widgetInfo = widget;
    };

    VAS.VAS_FinDInsights.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_FinDInsights.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
    };


})(VAS, jQuery);