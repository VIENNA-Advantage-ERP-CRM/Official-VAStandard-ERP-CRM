/************************************************************
 * Module Name    : VAS
 * Purpose        : Total Outstanding KPI Widget
 *                  Shows total outstanding AP balance, invoice count,
 *                  vendor count, trend vs last month, and a 7-month sparkline.
 * chronological  : Development
 * Created Date   : 13 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys used in this file (add via System Messages):
 *   VAS_TotalOutstanding  => "Total Outstanding"
 *   VAS_Invoices          => "Invoices"
 *   VAS_Vendors           => "Vendors"
 *   VAS_Worsening         => "worsening"
 *   VAS_Improving         => "improving"
 *   VAS_Crore             => "Cr"
 *   VAS_Lakh              => "L"
 *   VAS_Thousand          => "K"
 *   VAS_Million           => "M"
 *   VAS_Billion           => "B"
 *   VAS_Trillion          => "T"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_TotalOutstandingWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-towdg-root">');
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
                url: VIS.Application.contextUrl + 'VAS/VAS_TotalOutstandingWidget/GetTotalOutstandingKpi',
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
            $container = $('<div class="vas-towdg-container" id="vas_towdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render KPI content ---- */
        function renderKpi(data) {
            $container.empty();

            var sym = data.CurSymbol || '';
            var outstandingFormatted = formatAmount(data.OutstandingTotal, data.StdPrecision);

            var trendPct = 0;
            if (data.LastMonthOutstanding && data.LastMonthOutstanding !== 0) {
                trendPct = ((data.CurrentMonthOutstanding - data.LastMonthOutstanding) / Math.abs(data.LastMonthOutstanding)) * 100;
            } else if (data.CurrentMonthOutstanding > 0) {
                trendPct = 100;
            }

            // For outstanding: positive (more owed) = worsening; negative (less owed) = improving.
            // Arrow direction reflects financial health, not raw amount: worsening = down arrow.
            var isWorsening = trendPct >= 0;
            var trendClass = isWorsening ? 'vas-towdg-trend-down' : 'vas-towdg-trend-up';
            var trendSign = trendPct >= 0 ? '+' : '';
            var trendWord = isWorsening ? VIS.Msg.getMsg('VAS_Worsening') : VIS.Msg.getMsg('VAS_Improving');
            var arrowPoints = isWorsening ? '6 9 12 15 18 9' : '18 15 12 9 6 15';

            var sparkSvg = buildSparklineSvg(data.SparklineData || []);

            var html = '<div class="vas-towdg-label">' + VIS.Msg.getMsg('VAS_TotalOutstanding') + '</div>'
                + '<div class="vas-towdg-value" id="vas_towdg_val_' + widgetID + '">'
                +   sym + outstandingFormatted
                + '</div>'
                + '<div class="vas-towdg-pills-row">'
                +   '<div class="vas-towdg-pill">'
                +     '<span class="vas-towdg-pill-label">' + VIS.Msg.getMsg('VAS_Invoices') + '</span>'
                +     '<span class="vas-towdg-pill-value">' + data.InvoiceCount + '</span>'
                +   '</div>'
                +   '<div class="vas-towdg-pill">'
                +     '<span class="vas-towdg-pill-label">' + VIS.Msg.getMsg('VAS_Vendors') + '</span>'
                +     '<span class="vas-towdg-pill-value">' + data.VendorCount + '</span>'
                +   '</div>'
                + '</div>'
                + '<div class="vas-towdg-trend-row ' + trendClass + '">'
                +   '<svg class="vas-towdg-trend-icon" viewBox="0 0 24 24" fill="none"'
                +       ' stroke="currentColor" stroke-width="2.5"'
                +       ' stroke-linecap="round" stroke-linejoin="round">'
                +     '<polyline points="' + arrowPoints + '"/>'
                +   '</svg>'
                +   trendSign + Math.abs(trendPct).toFixed(1) + '% ' + trendWord
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
                unit = VIS.Msg.getMsg('VAS_Trillion');
                formatted = (absNumber / 1000000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000000000) {
                unit = VIS.Msg.getMsg('VAS_Billion');
                formatted = (absNumber / 1000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 10000000) {
                unit = VIS.Msg.getMsg('VAS_Crore');
                formatted = (absNumber / 10000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 100000) {
                unit = VIS.Msg.getMsg('VAS_Lakh');
                formatted = (absNumber / 100000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000) {
                unit = VIS.Msg.getMsg('VAS_Thousand');
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
            return '<svg class="vas-towdg-sparkline" width="' + W + '" height="' + H
                + '" viewBox="0 0 ' + W + ' ' + H + '">'
                + '<polyline points="' + pts.join(' ') + '" fill="none"'
                + ' stroke="#D14545" stroke-width="2.2" stroke-linecap="round"/>'
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
    VAS.VAS_TotalOutstandingWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_TotalOutstandingWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_TotalOutstandingWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_TotalOutstandingWidget.prototype.dispose = function () {
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
