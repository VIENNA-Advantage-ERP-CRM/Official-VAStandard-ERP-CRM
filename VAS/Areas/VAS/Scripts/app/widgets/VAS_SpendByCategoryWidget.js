/************************************************************
 * Module Name    : VAS
 * Purpose        : Spend by Category (MTD) Widget — horizontal bar + donut
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_SpendByCategoryMTD => "Spend by Category (MTD)"
 *   VAS_Bar                => "Bar"
 *   VAS_Donut              => "Donut"
 *   VAS_Total              => "Total"
 *   VAS_Crore              => "Cr"
 *   VAS_Lakh               => "L"
 *   VAS_Thousand           => "K"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* Category colour palette (12 distinct colours) */
    var CAT_COLORS = [
        '#1976D2', '#6A1B9A', '#2E7D32', '#E65100',
        '#8E7CC3', '#00796B', '#B71C1C', '#757575',
        '#AD1457', '#F57F17', '#006064', '#4E342E'
    ];

    VAS.VAS_SpendByCategoryWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self        = this;
        var $root        = $('<div class="h-100 w-100 vas-widget-bg vas-sbcwdg-root">');
        var $container;
        var widgetID     = null;
        var _currentView = 'bar';
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
                url: VIS.Application.contextUrl + 'VAS/VAS_SpendByCategoryWidget/GetSpendByCategory',
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

        function buildShell() {
            $container = $('<div class="vas-sbcwdg-container" id="vas_sbcwdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render HTML shell then draw chart ---- */
        function renderWidget(data) {
            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }
            $container.empty();
            _currentView = 'bar';

            var html = '<div class="vas-sbcwdg-header">'
                +   '<div class="vas-sbcwdg-title">'
                +     '<svg class="vas-sbcwdg-title-icon" viewBox="0 0 24 24" fill="none"'
                +         ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                +       '<rect x="2" y="3" width="4" height="18"/>'
                +       '<rect x="10" y="8" width="4" height="13"/>'
                +       '<rect x="18" y="1" width="4" height="20"/>'
                +     '</svg>'
                +     VIS.Msg.getMsg('VAS_SpendByCategoryMTD')
                +   '</div>'
                +   '<div class="vas-sbcwdg-tabs">'
                +     '<button class="vas-sbcwdg-tab vas-sbcwdg-tab-active" data-view="bar">'
                +       VIS.Msg.getMsg('VAS_Bar')
                +     '</button>'
                +     '<button class="vas-sbcwdg-tab" data-view="pie">'
                +       VIS.Msg.getMsg('VAS_Donut')
                +     '</button>'
                +   '</div>'
                + '</div>'
                + '<div class="vas-sbcwdg-chart-wrap">'
                +   '<canvas class="vas-sbcwdg-canvas" id="vas_sbcwdg_cv_' + widgetID + '"></canvas>'
                + '</div>';

            $container.html(html);

            $container.on('click', '.vas-sbcwdg-tab', function () {
                $container.find('.vas-sbcwdg-tab').removeClass('vas-sbcwdg-tab-active');
                $(this).addClass('vas-sbcwdg-tab-active');
                _currentView = $(this).data('view');
                drawChart(data);
            });

            window.setTimeout(function () { drawChart(data); }, 80);
        }

        /* ---- Master draw dispatcher ---- */
        function drawChart(data) {
            if (_currentView === 'pie') { drawDonut(data); } else { drawBar(data); }
        }

        /* ---- Chart.js Horizontal Bar ---- */
        function drawBar(data) {
            if (typeof Chart === 'undefined') { return; }
            var el = document.getElementById('vas_sbcwdg_cv_' + widgetID);
            if (!el) { return; }

            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }

            var cats = (data && data.Categories) || [];
            var sym  = (data && data.CurSymbol)  || '';
            var n    = cats.length;
            if (!n) { return; }

            var catNames   = cats.map(function (c) { return c.Name; });
            var catAmounts = cats.map(function (c) { return c.Amount; });
            var catColors  = cats.map(function (c, i) { return CAT_COLORS[i % CAT_COLORS.length]; });

            _chartInst = new Chart(el, {
                type: 'bar',
                data: {
                    labels: catNames,
                    datasets: [{
                        data: catAmounts,
                        backgroundColor: catColors,
                        borderRadius: 4,
                        borderSkipped: 'left',
                        barPercentage: 0.65,
                        categoryPercentage: 0.9
                    }]
                },
                options: {
                    indexAxis: 'y',
                    responsive: true,
                    maintainAspectRatio: false,
                    layout: { padding: { right: 8 } },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            callbacks: {
                                label: function (ctx) {
                                    return ' ' + sym + fmtShort(ctx.raw);
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { color: 'rgba(0,0,0,0.06)' },
                            ticks: {
                                font: { family: 'Roboto, sans-serif', size: 10 },
                                color: '#748494',
                                maxTicksLimit: 6,
                                callback: function (v) { return sym + fmtShort(v); }
                            },
                            border: { display: false }
                        },
                        y: {
                            grid: { display: false },
                            ticks: {
                                font: { family: 'Roboto, sans-serif', size: 10 },
                                color: '#5F7283'
                            },
                            border: { display: false }
                        }
                    }
                }
            });
        }

        /* ---- Chart.js Donut ---- */
        function drawDonut(data) {
            if (typeof Chart === 'undefined') { return; }
            var el = document.getElementById('vas_sbcwdg_cv_' + widgetID);
            if (!el) { return; }

            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }

            var cats = (data && data.Categories) || [];
            var sym  = (data && data.CurSymbol)  || '';
            var n    = cats.length;
            if (!n) { return; }

            var total = 0;
            for (var ti = 0; ti < n; ti++) { total += cats[ti].Amount; }
            if (total <= 0) { return; }

            var catNames   = cats.map(function (c) { return c.Name; });
            var catAmounts = cats.map(function (c) { return c.Amount; });
            var catColors  = cats.map(function (c, i) { return CAT_COLORS[i % CAT_COLORS.length]; });

            var totalLabel = VIS.Msg.getMsg('VAS_Total') || 'Total';
            var centerText = sym + fmtShort(total);

            var centerPlugin = {
                id: 'vas_sbc_center_' + widgetID,
                afterDraw: function (chart) {
                    var ctx = chart.ctx;
                    var cx  = (chart.chartArea.left + chart.chartArea.right)  / 2;
                    var cy  = (chart.chartArea.top  + chart.chartArea.bottom) / 2;
                    var fs  = Math.max(9, Math.min(11, chart.width / 120));
                    ctx.save();
                    ctx.textAlign    = 'center';
                    ctx.textBaseline = 'middle';
                    ctx.fillStyle    = '#102C3F';
                    ctx.font         = 'bold ' + (fs + 1) + 'px Roboto, sans-serif';
                    ctx.fillText(centerText, cx, cy - fs * 0.65);
                    ctx.font      = fs + 'px Roboto, sans-serif';
                    ctx.fillStyle = '#748494';
                    ctx.fillText(totalLabel, cx, cy + fs * 0.85);
                    ctx.restore();
                }
            };

            _chartInst = new Chart(el, {
                type: 'doughnut',
                data: {
                    labels: catNames,
                    datasets: [{
                        data: catAmounts,
                        backgroundColor: catColors,
                        borderWidth: 2,
                        borderColor: 'rgba(255,255,255,0.8)',
                        hoverBorderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: '55%',
                    layout: { padding: 4 },
                    plugins: {
                        legend: {
                            position: 'right',
                            labels: {
                                font: { family: 'Roboto, sans-serif', size: 10 },
                                color: '#5F7283',
                                boxWidth: 10,
                                boxHeight: 10,
                                padding: 8,
                                generateLabels: function () {
                                    return catNames.map(function (name, i) {
                                        var pct   = Math.round((catAmounts[i] / total) * 100);
                                        var trunc = name.length > 14 ? name.slice(0, 14) + '…' : name;
                                        return {
                                            text: trunc + '  ' + sym + fmtShort(catAmounts[i]) + '  ' + pct + '%',
                                            fillStyle:   catColors[i],
                                            strokeStyle: catColors[i],
                                            hidden: false,
                                            index: i,
                                            datasetIndex: 0
                                        };
                                    });
                                }
                            }
                        },
                        tooltip: {
                            callbacks: {
                                label: function (ctx) {
                                    var i   = ctx.dataIndex;
                                    var pct = Math.round((catAmounts[i] / total) * 100);
                                    return ' ' + sym + fmtShort(catAmounts[i]) + '  (' + pct + '%)';
                                }
                            }
                        }
                    }
                },
                plugins: [centerPlugin]
            });
        }

        /* ---- Compact amount formatter ---- */
        function fmtShort(val) {
            if (val === 0 || !val) { return '0'; }
            var abs   = Math.abs(val);
            var loc   = window.navigator.language;
            var opts1 = { minimumFractionDigits: 1, maximumFractionDigits: 1 };
            if (abs >= 10000000) { return (abs / 10000000).toLocaleString(loc, opts1) + VIS.Msg.getMsg('VAS_Crore'); }
            if (abs >= 100000)   { return (abs / 100000).toLocaleString(loc, opts1)   + VIS.Msg.getMsg('VAS_Lakh'); }
            if (abs >= 1000)     { return (abs / 1000).toLocaleString(loc, opts1)     + VIS.Msg.getMsg('VAS_Thousand'); }
            return abs.toLocaleString(loc, { maximumFractionDigits: 0 });
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
    VAS.VAS_SpendByCategoryWidget.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_SpendByCategoryWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_SpendByCategoryWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
        var self = this;
        if (self._kpiData && self._drawChart) {
            window.setTimeout(function () { self._drawChart(self._kpiData); }, 100);
        }
    };

    VAS.VAS_SpendByCategoryWidget.prototype.dispose = function () {
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
