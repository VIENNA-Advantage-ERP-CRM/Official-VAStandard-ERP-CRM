/************************************************************
 * Module Name    : VAS
 * Purpose        : Avg Payment Period (DPO) KPI Widget
 *                  Shows average days payable outstanding, target vs gap,
 *                  trend vs last month in days, and a 7-month sparkline.
 * chronological  : Development
 * Created Date   : 13 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys used in this file (add via System Messages):
 *   VAS_AvgPaymentPeriodDpo  => "Avg Payment Period (DPO)"
 *   VAS_Target               => "Target"
 *   VAS_Gap                  => "Gap"
 *   VAS_Days                 => "days"
 *   VAS_DaySuffix            => "d"
 *   VAS_VsLastMonth          => "vs last month"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_AvgPaymentPeriodWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-apwdg-root">');
        var $container;
        var widgetID = null;

        /* ---- Initialise ---- */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) !== 0
                ? this.widgetInfo.AD_UserHomeWidgetID
                : $self.windowNo);
            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'visible';
        };

        /* ---- Data load ---- */
        this.intialLoad = function () {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_AvgPaymentPeriodWidget/GetAvgPaymentPeriodKpi',
                dataType: 'json',
                async: true,
                success: function (data) {
                    var kpiData = typeof data === 'string' ? JSON.parse(data) : data;
                    if (kpiData) {
                        renderKpi(kpiData);
                    }
                    $bsyDiv[0].style.visibility = 'hidden';
                },
                error: function () {
                    $bsyDiv[0].style.visibility = 'hidden';
                }
            });
        };

        /* ---- Build the root shell ---- */
        function buildShell() {
            $container = $('<div class="vas-apwdg-container" id="vas_apwdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render KPI content ---- */
        function renderKpi(data) {
            $container.empty();

            var daySuffix = VIS.Msg.getMsg('VAS_DaySuffix');
            var days = VIS.Msg.getMsg('VAS_Days');

            // Trend: difference in days (absolute, not percentage)
            var daysDiff = (data.CurrentMonthDpo || 0) - (data.LastMonthDpo || 0);
            var isIncreasing = daysDiff >= 0;
            var trendClass = isIncreasing ? 'vas-apwdg-trend-warn' : 'vas-apwdg-trend-good';
            var trendSign = isIncreasing ? '+' : '';
            var arrowPoints = isIncreasing ? '18 15 12 9 6 15' : '6 9 12 15 18 9';

            // Gap pill: amber when over target, green when at or under
            var gapDays = data.GapDays || 0;
            var gapSign = gapDays > 0 ? '+' : '';
            var gapClass = gapDays > 0 ? 'vas-apwdg-pill-value-warn' : 'vas-apwdg-pill-value-good';

            var sparkSvg = buildSparklineSvg(data.SparklineData || []);

            var html = '<div class="vas-apwdg-label">' + VIS.Msg.getMsg('VAS_AvgPaymentPeriodDpo') + '</div>'
                + '<div class="vas-apwdg-value" id="vas_apwdg_val_' + widgetID + '">'
                +   (data.CurrentMonthDpo || 0) + ' ' + days
                + '</div>'
                + '<div class="vas-apwdg-pills-row">'
                +   '<div class="vas-apwdg-pill">'
                +     '<span class="vas-apwdg-pill-label">' + VIS.Msg.getMsg('VAS_Target') + '</span>'
                +     '<span class="vas-apwdg-pill-value">' + (data.TargetDpo || 0) + daySuffix + '</span>'
                +   '</div>'
                +   '<div class="vas-apwdg-pill">'
                +     '<span class="vas-apwdg-pill-label">' + VIS.Msg.getMsg('VAS_Gap') + '</span>'
                +     '<span class="vas-apwdg-pill-value ' + gapClass + '">' + gapSign + gapDays + daySuffix + '</span>'
                +   '</div>'
                + '</div>'
                + '<div class="vas-apwdg-trend-row ' + trendClass + '">'
                +   '<svg class="vas-apwdg-trend-icon" viewBox="0 0 24 24" fill="none"'
                +       ' stroke="currentColor" stroke-width="2.5"'
                +       ' stroke-linecap="round" stroke-linejoin="round">'
                +     '<polyline points="' + arrowPoints + '"/>'
                +   '</svg>'
                +   trendSign + Math.abs(daysDiff) + ' ' + days + ' ' + VIS.Msg.getMsg('VAS_VsLastMonth')
                + '</div>'
                + sparkSvg;

            $container.append(html);
        }

        /* ---- Build sparkline SVG ---- */
        function buildSparklineSvg(data) {
            if (!data || data.length < 2) { return ''; }
            var W = 90, H = 48, pad = 3;
            var maxVal = Math.max.apply(null, data);
            var minVal = Math.min.apply(null, data);
            if (maxVal === minVal) { maxVal = minVal + 1; }
            var xStep = (W - pad * 2) / (data.length - 1);
            var pts = [];
            for (var i = 0; i < data.length; i++) {
                var x = pad + i * xStep;
                var y = H - pad - ((data[i] - minVal) / (maxVal - minVal)) * (H - pad * 2);
                pts.push(x.toFixed(1) + ',' + y.toFixed(1));
            }
            return '<svg class="vas-apwdg-sparkline" width="' + W + '" height="' + H
                + '" viewBox="0 0 ' + W + ' ' + H + '">'
                + '<polyline points="' + pts.join(' ') + '" fill="none"'
                + ' stroke="#8B7CFF" stroke-width="2.2" stroke-linecap="round"/>'
                + '</svg>';
        }

        /* ---- Busy indicator ---- */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'visible';
            $root.append($bsyDiv);
        }

        /* ---- Refresh ---- */
        this.refreshWidget = function () {
            $bsyDiv[0].style.visibility = 'visible';
            $container.empty();
            $self.intialLoad();
        };

        this.getRoot = function () {
            return $root;
        };
    };

    /* ---- Prototype ---- */
    VAS.VAS_AvgPaymentPeriodWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () {
            self.intialLoad();
        }, 50);
    };

    VAS.VAS_AvgPaymentPeriodWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_AvgPaymentPeriodWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_AvgPaymentPeriodWidget.prototype.dispose = function () {
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
