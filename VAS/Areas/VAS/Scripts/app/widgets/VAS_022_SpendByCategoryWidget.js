/************************************************************
 * Module Name    : VAS
 * Purpose        : Spend by Category (MTD) Widget — horizontal bar + donut
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_022_SpendByCategoryMTD => "Spend by Category (MTD)"
 *   VAS_022_Bar                => "Bar"
 *   VAS_022_Donut              => "Donut"
 *   VAS_022_Total              => "Total"
 *   VAS_022_Crore              => "Cr"
 *   VAS_022_Lakh               => "L"
 *   VAS_022_Thousand           => "K"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* ---- Message helper: returns the AD_Message text, or the inline default
         when the system has no message for the key. ---- */
    function msg(key, fallback) {
        var value = VIS.Msg.getMsg(key);
        return value && value !== key && value !== '[' + key + ']' ? value : fallback;
    }

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the em-anchor clamps resolve against the dashboard's visible
       width, not the viewport. One document-level ResizeObserver serves every widget;
       without a marked container — or without ResizeObserver — the CSS falls back to
       100vw. (Mirrors VAS_024_TotalOutstandingWidget.) */
    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    /* Category colour palette (12 distinct colours) */
    var CAT_COLORS = [
        '#1976D2', '#6A1B9A', '#2E7D32', '#E65100',
        '#8E7CC3', '#00796B', '#B71C1C', '#757575',
        '#AD1457', '#F57F17', '#006064', '#4E342E'
    ];

    VAS.VAS_022_SpendByCategoryWidget = function () {
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
                url: VIS.Application.contextUrl + 'VAS/VAS_022_SpendByCategoryWidget/GetSpendByCategory',
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
                +     msg('VAS_022_SpendByCategoryMTD', 'Spend by Category (MTD)')
                +   '</div>'
                +   '<div class="vas-sbcwdg-tabs">'
                +     '<button class="vas-sbcwdg-tab vas-sbcwdg-tab-active" data-view="bar">'
                +       msg('VAS_022_Bar', 'Bar')
                +     '</button>'
                +     '<button class="vas-sbcwdg-tab" data-view="pie">'
                +       msg('VAS_022_Donut', 'Donut')
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
                                    return ' ' + sym + fmtShort(ctx.raw, data.StdPrecision);
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { color: 'rgba(0,0,0,0.06)' },
                            ticks: {
                                font: { family: 'Roboto, sans-serif', size: 10 },
                                color: '#5F7283',
                                maxTicksLimit: 6,
                                callback: function (v) { return sym + fmtShort(v, data.StdPrecision); }
                            },
                            border: { display: false }
                        },
                        y: {
                            grid: { display: false },
                            ticks: {
                                font: { family: 'Roboto, sans-serif', size: 10 },
                                color: '#41576A'
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

            var totalLabel = msg('VAS_022_Total', 'Total');
            var centerText = sym + fmtShort(total, data.StdPrecision);

            // Legend font scales with the chart width so it's larger and stays responsive to
            // widget resize / zoom (redrawn via widgetSizeChange; canvas scales on browser zoom).
            var legFs = Math.max(11, Math.min(15, Math.round((el.parentNode ? el.parentNode.clientWidth : 300) * 0.03)));

            var centerPlugin = {
                id: 'VAS_022_sbc_center_' + widgetID,
                afterDraw: function (chart) {
                    var ctx = chart.ctx;
                    var cx  = (chart.chartArea.left + chart.chartArea.right)  / 2;
                    var cy  = (chart.chartArea.top  + chart.chartArea.bottom) / 2;
                    /* Scale with the rendered donut size so the center text is larger and keeps
                       its proportion on zoom/resize (fs = "Total" label; amount is larger). */
                    var fs    = Math.max(11, Math.min(18, chart.width / 11));
                    var amtFs = fs + 2;
                    ctx.save();
                    ctx.textAlign    = 'center';
                    ctx.textBaseline = 'middle';
                    ctx.fillStyle    = '#102C3F';
                    ctx.font         = 'bold ' + amtFs + 'px Roboto, sans-serif';
                    ctx.fillText(centerText, cx, cy - fs * 0.55);
                    ctx.font      = fs + 'px Roboto, sans-serif';
                    ctx.fillStyle = '#5F7283';
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
                    // Shrink the ring within its area so the donut isn't as tall as the
                    // full chart height — leaves a little breathing room top/bottom.
                    radius: '82%',
                    layout: { padding: 4 },
                    plugins: {
                        legend: {
                            position: 'right',
                            labels: {
                                font: { family: 'Roboto, sans-serif', size: legFs },
                                color: '#41576A',
                                boxWidth: legFs,
                                boxHeight: legFs,
                                padding: 8,
                                generateLabels: function () {
                                    return catNames.map(function (name, i) {
                                        return {
                                            /* Category name only (amount / % are shown on hover). */
                                            text: name,
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
                                    return ' ' + sym + fmtShort(catAmounts[i], data.StdPrecision) + '  (' + pct + '%)';
                                }
                            }
                        }
                    }
                },
                plugins: [centerPlugin]
            });
        }

        /* ---- Compact amount formatter ----
             Delegates the compact scaling (Indian vs international, per base-currency ISO) to
             the shared CurrencyFormat util VIS.Util.formatCompactAmount, exactly like
             VAS_019/VAS_023. Returns the unsigned magnitude; callers prepend the symbol. */
        function fmtShort(val, stdPrecision) {
            var d = $self._kpiData || {};
            return VIS.Util.formatCompactAmount(val, d.CurIso || '', (d.StdPrecision != null ? d.StdPrecision : stdPrecision));
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
    VAS.VAS_022_SpendByCategoryWidget.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        // Self-wire the dashboard-width CSS variable (--dash-inline-size) the clamps read.
        ensureDashInlineSizeVar(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_022_SpendByCategoryWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_022_SpendByCategoryWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
        var self = this;
        if (self._kpiData && self._drawChart) {
            window.setTimeout(function () { self._drawChart(self._kpiData); }, 100);
        }
    };

    VAS.VAS_022_SpendByCategoryWidget.prototype.dispose = function () {
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
