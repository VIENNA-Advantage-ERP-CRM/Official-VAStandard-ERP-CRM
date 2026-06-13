/************************************************************
 * Module Name    : VAS
 * Purpose        : Monthly Purchase Spend Trend Widget (line chart)
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_017_MonthlyPurchaseSpend => "Monthly Purchase Spend"
 *   VAS_017_ThisYear             => "This year"
 *   VAS_017_LastYear             => "Last year"
 *   VAS_017_Monthly              => "Monthly"
 *   VAS_017_Quarterly            => "Quarterly"
 *   VAS_017_Q1                   => "Q1"
 *   VAS_017_Q2                   => "Q2"
 *   VAS_017_Q3                   => "Q3"
 *   VAS_017_Q4                   => "Q4"
 *   VAS_017_Crore                => "Cr"
 *   VAS_017_Lakh                 => "L"
 *   VAS_017_Thousand             => "K"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_017_MonthlyPurchaseSpendWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self        = this;
        var $root        = $('<div class="h-100 w-100 vas-widget-bg vas-mpswdg-root">');
        var $container;
        var widgetID     = null;
        var _currentMode = 'monthly';
        var _chartInst   = null;

        $self._kpiData   = null;
        $self._drawChart = null;

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
                url: VIS.Application.contextUrl + 'VAS/VAS_017_MonthlyPurchaseSpendWidget/GetMonthlyPurchaseSpend',
                dataType: 'json',
                async: true,
                success: function (data) {
                    $self._kpiData = typeof data === 'string' ? JSON.parse(data) : data;
                    if ($self._kpiData) { renderWidget($self._kpiData); }
                    $bsyDiv[0].style.visibility = 'hidden';
                },
                error: function () {
                    $bsyDiv[0].style.visibility = 'hidden';
                }
            });
        };

        /* ---- Shell ---- */
        function buildShell() {
            $container = $('<div class="vas-mpswdg-container" id="vas_mpswdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render HTML frame then draw chart ---- */
        function renderWidget(data) {
            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }
            $container.empty();
            _currentMode = 'monthly';

            var fyLbl = data.FyLabel || '';

            var html = '<div class="vas-mpswdg-header">'
                +   '<div class="vas-mpswdg-title">'
                +     '<svg class="vas-mpswdg-title-icon" viewBox="0 0 24 24" fill="none"'
                +         ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                +       '<polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/>'
                +       '<polyline points="17 6 23 6 23 12"/>'
                +     '</svg>'
                +     VIS.Msg.getMsg('VAS_017_MonthlyPurchaseSpend') + ' — ' + fyLbl
                +   '</div>'
                +   '<div class="vas-mpswdg-header-right">'
                +     '<div class="vas-mpswdg-legend">'
                +       '<span class="vas-mpswdg-legend-item">'
                +         '<span class="vas-mpswdg-leg-line vas-mpswdg-leg-solid"></span>'
                +         VIS.Msg.getMsg('VAS_017_ThisYear')
                +       '</span>'
                +       '<span class="vas-mpswdg-legend-item">'
                +         '<span class="vas-mpswdg-leg-line vas-mpswdg-leg-dashed"></span>'
                +         VIS.Msg.getMsg('VAS_017_LastYear')
                +       '</span>'
                +     '</div>'
                +     '<div class="vas-mpswdg-tabs">'
                +       '<button class="vas-mpswdg-tab vas-mpswdg-tab-active" data-mode="monthly">'
                +         VIS.Msg.getMsg('VAS_017_Monthly')
                +       '</button>'
                +       '<button class="vas-mpswdg-tab" data-mode="quarterly">'
                +         VIS.Msg.getMsg('VAS_017_Quarterly')
                +       '</button>'
                +     '</div>'
                +   '</div>'
                + '</div>'
                + '<div class="vas-mpswdg-chart-wrap">'
                +   '<canvas class="vas-mpswdg-canvas" id="vas_mpswdg_cv_' + widgetID + '"></canvas>'
                + '</div>';

            $container.html(html);

            $container.on('click', '.vas-mpswdg-tab', function () {
                $container.find('.vas-mpswdg-tab').removeClass('vas-mpswdg-tab-active');
                $(this).addClass('vas-mpswdg-tab-active');
                _currentMode = $(this).data('mode');
                drawChart(data);
            });

            window.setTimeout(function () { drawChart(data); }, 80);
        }

        /* ---- Chart.js Line Chart ---- */
        function drawChart(data) {
            if (typeof Chart === 'undefined') { return; }
            var el = document.getElementById('vas_mpswdg_cv_' + widgetID);
            if (!el) { return; }

            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }

            var cy  = data.CurrentYear || [];
            var ly  = data.LastYear    || [];
            var sym = data.CurSymbol   || '';
            var d1, d2, xLabels;

            if (_currentMode === 'quarterly') {
                d1 = [
                    cy[0] + cy[1] + cy[2],
                    cy[3] + cy[4] + cy[5],
                    cy[6] + cy[7] + cy[8],
                    cy[9] + cy[10] + cy[11]
                ];
                d2 = [
                    ly[0] + ly[1] + ly[2],
                    ly[3] + ly[4] + ly[5],
                    ly[6] + ly[7] + ly[8],
                    ly[9] + ly[10] + ly[11]
                ];
                xLabels = [
                    VIS.Msg.getMsg('VAS_017_Q1'),
                    VIS.Msg.getMsg('VAS_017_Q2'),
                    VIS.Msg.getMsg('VAS_017_Q3'),
                    VIS.Msg.getMsg('VAS_017_Q4')
                ];
            } else {
                /* Indian FY: Apr(3)→Mar(2); generate locale-aware month abbreviations */
                var fyMonths = [3, 4, 5, 6, 7, 8, 9, 10, 11, 0, 1, 2];
                xLabels = fyMonths.map(function (m) {
                    return new Date(2000, m, 1).toLocaleDateString(window.navigator.language, { month: 'short' });
                });
                d1 = cy.slice(0, 12);
                d2 = ly.slice(0, 12);
            }

            /* Replace zeros with null so Chart.js creates gaps (spanGaps: false) */
            d1 = d1.map(function (v) { return v > 0 ? v : null; });
            d2 = d2.map(function (v) { return v > 0 ? v : null; });

            _chartInst = new Chart(el, {
                type: 'line',
                data: {
                    labels: xLabels,
                    datasets: [
                        {
                            label: VIS.Msg.getMsg('VAS_017_ThisYear') || 'This year',
                            data: d1,
                            borderColor: '#0083DA',
                            backgroundColor: 'rgba(0,131,218,0.10)',
                            fill: true,
                            tension: 0.4,
                            spanGaps: false,
                            pointRadius: 3,
                            pointBackgroundColor: '#ffffff',
                            pointBorderColor: '#0083DA',
                            pointBorderWidth: 1.5,
                            borderWidth: 2
                        },
                        {
                            label: VIS.Msg.getMsg('VAS_017_LastYear') || 'Last year',
                            data: d2,
                            borderColor: 'rgba(0,131,218,0.38)',
                            backgroundColor: 'transparent',
                            fill: false,
                            tension: 0.4,
                            spanGaps: false,
                            borderDash: [5, 4],
                            pointRadius: 0,
                            borderWidth: 1.5
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    layout: { padding: { top: 4, right: 8 } },
                    interaction: {
                        mode: 'index',
                        intersect: false
                    },
                    plugins: {
                        legend: { display: false },   /* custom HTML legend in header */
                        tooltip: {
                            mode: 'index',
                            intersect: false,
                            callbacks: {
                                label: function (ctx) {
                                    if (ctx.raw === null) { return null; }
                                    return ' ' + ctx.dataset.label + ': ' + sym + fmtShort(ctx.raw, data.StdPrecision);
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { display: false },
                            ticks: {
                                font: { family: 'Roboto, sans-serif', size: 10 },
                                color: '#748494'
                            },
                            border: { display: false }
                        },
                        y: {
                            grid: { color: 'rgba(0,0,0,0.06)' },
                            ticks: {
                                font: { family: 'Roboto, sans-serif', size: 10 },
                                color: '#748494',
                                maxTicksLimit: 6,
                                callback: function (v) { return sym + fmtShort(v, data.StdPrecision); }
                            },
                            border: { display: false }
                        }
                    }
                }
            });
        }

        /* ---- Compact amount formatter ---- */
        function fmtShort(val, stdPrecision) {
            if (!val || val === 0) { return '0'; }
            var abs   = Math.abs(val);
            var loc   = window.navigator.language;
            var prec  = VIS.Env.getCtx().getStdPrecision() || stdPrecision || 2;
            var opts1 = { minimumFractionDigits: 1, maximumFractionDigits: 1 };
            if (abs >= 10000000) { return (abs / 10000000).toLocaleString(loc, opts1) + VIS.Msg.getMsg('VAS_017_Crore'); }
            if (abs >= 100000)   { return (abs / 100000).toLocaleString(loc, opts1)   + VIS.Msg.getMsg('VAS_017_Lakh'); }
            if (abs >= 1000)     { return (abs / 1000).toLocaleString(loc, opts1)     + VIS.Msg.getMsg('VAS_017_Thousand'); }
            return abs.toLocaleString(loc, { minimumFractionDigits: prec, maximumFractionDigits: prec });
        }

        /* ---- Busy indicator ---- */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'visible';
            $root.append($bsyDiv);
        }

        /* ---- Refresh ---- */
        this.refreshWidget = function () {
            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }
            $self._kpiData = null;
            $bsyDiv[0].style.visibility = 'visible';
            $container.empty();
            $self.intialLoad();
        };

        this.getRoot = function () { return $root; };

        $self._drawChart = drawChart;
    };

    /* ---- Prototype ---- */
    VAS.VAS_017_MonthlyPurchaseSpendWidget.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_017_MonthlyPurchaseSpendWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_017_MonthlyPurchaseSpendWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
        var self = this;
        if (self._kpiData && self._drawChart) {
            window.setTimeout(function () { self._drawChart(self._kpiData); }, 100);
        }
    };

    VAS.VAS_017_MonthlyPurchaseSpendWidget.prototype.dispose = function () {
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
