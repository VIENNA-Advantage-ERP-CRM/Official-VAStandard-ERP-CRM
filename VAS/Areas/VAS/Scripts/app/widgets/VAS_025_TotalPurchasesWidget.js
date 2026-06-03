/************************************************************
 * Module Name    : VAS
 * Purpose        : Total Purchases (MTD) KPI Widget
 *                  Shows MTD total, YTD total, invoice count,
 *                  trend vs last month, and a 7-month sparkline.
 * chronological  : Development
 * Created Date   : 12 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys used in this file (add via System Messages):
 *   VAS_025_TotalPurchasesMTD  => "Total Purchases (MTD)"
 *   VAS_025_YTD                => "YTD"
 *   VAS_025_INV                => "INV"
 *   VAS_025_VsLastMonth        => "vs last month"
 *   VAS_025_Crore              => "Cr"
 *   VAS_025_Lakh               => "L"
 *   VAS_025_Thousand           => "K"
 *   VAS_025_Million            => "M"
 *   VAS_025_Billion            => "B"
 *   VAS_025_Trillion           => "T"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_025_TotalPurchasesWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-tpwidg-root">');
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
                url: VIS.Application.contextUrl + 'VAS/VAS_025_TotalPurchasesWidget/GetTotalPurchasesKpi',
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

        /* ---- Build the root shell (empty container, filled after load) ---- */
        function buildShell() {
            $container = $('<div class="vas-tpwidg-container" id="vas_tpwidg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render KPI content ---- */
        function renderKpi(data) {
            $container.empty();

            var sym = data.CurSymbol || '';
            var mtdFormatted = formatAmount(data.MtdTotal, data.StdPrecision);
            var ytdFormatted = formatAmount(data.YtdTotal, data.StdPrecision);

            var trendPct = 0;
            if (data.LastMonthTotal && data.LastMonthTotal !== 0) {
                trendPct = ((data.MtdTotal - data.LastMonthTotal) / Math.abs(data.LastMonthTotal)) * 100;
            } else if (data.MtdTotal > 0) {
                trendPct = 100;
            }
            var trendUp = trendPct >= 0;
            var trendClass = trendUp ? 'vas-tpwidg-trend-up' : 'vas-tpwidg-trend-down';
            var trendSign = trendUp ? '+' : '';
            // Up arrow: 18,15 → 12,9 → 6,15  |  Down arrow: 6,9 → 12,15 → 18,9
            var arrowPoints = trendUp ? '18 15 12 9 6 15' : '6 9 12 15 18 9';

            var sparkSvg = buildSparklineSvg(data.SparklineData || []);

            var html = '<div class="vas-tpwidg-label">' + VIS.Msg.getMsg('VAS_025_TotalPurchasesMTD') + '</div>'
                + '<div class="vas-tpwidg-value" id="vas_tpwidg_val_' + widgetID + '">'
                +   sym + mtdFormatted
                + '</div>'
                + '<div class="vas-tpwidg-pills-row">'
                +   '<div class="vas-tpwidg-pill">'
                +     '<span class="vas-tpwidg-pill-label">' + VIS.Msg.getMsg('VAS_025_YTD') + '</span>'
                +     '<span class="vas-tpwidg-pill-value">' + sym + ytdFormatted + '</span>'
                +   '</div>'
                +   '<div class="vas-tpwidg-pill">'
                +     '<span class="vas-tpwidg-pill-label">' + VIS.Msg.getMsg('VAS_025_INV') + '</span>'
                +     '<span class="vas-tpwidg-pill-value">' + data.InvoiceCount + '</span>'
                +   '</div>'
                + '</div>'
                + '<div class="vas-tpwidg-trend-row ' + trendClass + '">'
                +   '<svg class="vas-tpwidg-trend-icon" viewBox="0 0 24 24" fill="none"'
                +       ' stroke="currentColor" stroke-width="2.5"'
                +       ' stroke-linecap="round" stroke-linejoin="round">'
                +     '<polyline points="' + arrowPoints + '"/>'
                +   '</svg>'
                +   trendSign + Math.abs(trendPct).toFixed(1) + '% ' + VIS.Msg.getMsg('VAS_025_VsLastMonth')
                + '</div>'
                + sparkSvg;

            $container.append(html);
        }

        /* ---- Number formatter: Trillion / Billion / Crore / Lakh / Thousand / raw ---- */
        function formatAmount(number, stdPrecision) {
            var prec = VIS.Env.getCtx().getStdPrecision() || stdPrecision || 2;
            var isNegative = number < 0;
            var absNumber = Math.abs(number);
            var formatted;
            var unit = '';
            var opts2 = { minimumFractionDigits: 2, maximumFractionDigits: 2 };
            var optsRaw = { minimumFractionDigits: prec, maximumFractionDigits: prec };

            if (absNumber >= 1000000000000) {
                unit = VIS.Msg.getMsg('VAS_025_Trillion');
                formatted = (absNumber / 1000000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000000000) {
                unit = VIS.Msg.getMsg('VAS_025_Billion');
                formatted = (absNumber / 1000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 10000000) {
                unit = VIS.Msg.getMsg('VAS_025_Crore');
                formatted = (absNumber / 10000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 100000) {
                unit = VIS.Msg.getMsg('VAS_025_Lakh');
                formatted = (absNumber / 100000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000) {
                unit = VIS.Msg.getMsg('VAS_025_Thousand');
                formatted = (absNumber / 1000).toLocaleString(window.navigator.language, opts2);
            } else {
                formatted = absNumber.toLocaleString(window.navigator.language, optsRaw);
            }

            return (isNegative ? '-' : '') + formatted + unit;
        }

        /* ---- Build sparkline SVG from monthly data array ---- */
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
            return '<svg class="vas-tpwidg-sparkline" width="' + W + '" height="' + H
                + '" viewBox="0 0 ' + W + ' ' + H + '">'
                + '<polyline points="' + pts.join(' ') + '" fill="none"'
                + ' stroke="#0083DA" stroke-width="2.2" stroke-linecap="round"/>'
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
    VAS.VAS_025_TotalPurchasesWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_025_TotalPurchasesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_025_TotalPurchasesWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_025_TotalPurchasesWidget.prototype.dispose = function () {
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
