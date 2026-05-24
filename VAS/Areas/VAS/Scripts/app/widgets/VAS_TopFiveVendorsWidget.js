/************************************************************
 * Module Name    : VAS
 * Purpose        : Top Five Vendors by Spend Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_Top5VendorsBySpend => "Top 5 Vendors by Spend"
 *   VAS_VendorSpendDist    => "Vendor Spend Distribution"
 *   VAS_Total              => "Total"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* Per-rank avatar palette */
    var AVATAR_COLORS = [
        { bg: '#EAF8FF', text: '#0E5DA8' },
        { bg: '#EEEAFF', text: '#4A3A9A' },
        { bg: '#FFF0D0', text: '#7A4A00' },
        { bg: '#FAD7D7', text: '#8F2D2D' },
        { bg: '#CCEFDD', text: '#0C5D38' }
    ];

    /* Vibrant segment colours for the donut chart */
    var DONUT_COLORS = ['#1F83FF', '#7B68EE', '#FF9500', '#E84040', '#00B894'];

    VAS.VAS_TopFiveVendorsWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self    = this;
        var $root    = $('<div class="h-100 w-100 vas-widget-bg vas-t5vwdg-root">');
        var $container;
        var widgetID  = null;
        var _chartInst = null;

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
                url: VIS.Application.contextUrl + 'VAS/VAS_TopFiveVendorsWidget/GetTopFiveVendors',
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
            $container = $('<div class="vas-t5vwdg-container" id="vas_t5vwdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render HTML + trigger Chart.js donut ---- */
        function renderWidget(data) {
            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }
            $container.empty();

            var vendors = (data && data.Vendors)   ? data.Vendors   : [];
            var sym     = (data && data.CurSymbol) ? data.CurSymbol : '';
            var fyLabel = (data && data.FyLabel)   ? data.FyLabel   : '';

            var html =
                '<div class="vas-t5vwdg-header">' +
                    '<div class="vas-t5vwdg-title">' +
                        '<svg class="vas-t5vwdg-title-icon" viewBox="0 0 24 24" fill="none"' +
                            ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                            '<rect x="2" y="7" width="20" height="14" rx="2" ry="2"/>' +
                            '<path d="M16 7V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2"/>' +
                        '</svg>' +
                        t5vEsc(VIS.Msg.getMsg('VAS_Top5VendorsBySpend') || 'Top 5 Vendors by Spend') +
                    '</div>' +
                    '<span class="vas-t5vwdg-fy-chip">' + t5vEsc(fyLabel) + '</span>' +
                '</div>' +
                '<div class="vas-t5vwdg-list">';

            for (var i = 0; i < vendors.length; i++) {
                var v   = vendors[i];
                var pos = v.YoyPct >= 0;
                var rankCls = 'vas-t5vwdg-avatar--r' + Math.min(v.Rank, 5);
                html +=
                    '<div class="vas-t5vwdg-row">' +
                        '<span class="vas-t5vwdg-rank">' + v.Rank + '</span>' +
                        '<span class="vas-t5vwdg-avatar ' + rankCls + '">' +
                            t5vEsc(v.Initials) +
                        '</span>' +
                        '<div class="vas-t5vwdg-info">' +
                            '<div class="vas-t5vwdg-name">' + t5vEsc(v.Name)     + '</div>' +
                            '<div class="vas-t5vwdg-cat">'  + t5vEsc(v.Category) + '</div>' +
                        '</div>' +
                        '<div class="vas-t5vwdg-metrics">' +
                            '<div class="vas-t5vwdg-amt">' + t5vFmt(v.CurrAmt, sym, data.StdPrecision) + '</div>' +
                            '<div class="vas-t5vwdg-yoy ' + (pos ? 'vas-t5vwdg-yoy-pos' : 'vas-t5vwdg-yoy-neg') + '">' +
                                t5vEsc((pos ? '+' : '') + v.YoyPct + '%') +
                            '</div>' +
                        '</div>' +
                    '</div>';
            }

            html +=
                '</div>' +
                '<div class="vas-t5vwdg-divider"></div>' +
                '<div class="vas-t5vwdg-chart-header">' +
                    '<svg class="vas-t5vwdg-chart-icon" viewBox="0 0 24 24" fill="none"' +
                        ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                        '<polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>' +
                    '</svg>' +
                    t5vEsc(VIS.Msg.getMsg('VAS_VendorSpendDist') || 'Vendor Spend Distribution') +
                '</div>' +
                '<div class="vas-t5vwdg-chart-wrap">' +
                    '<canvas class="vas-t5vwdg-canvas" id="vas_t5vwdg_cv_' + widgetID + '"></canvas>' +
                '</div>';

            $container.html(html);
            window.setTimeout(function () { drawDonut(data); }, 80);
        }

        /* ---- Chart.js Donut ---- */
        function drawDonut(data) {
            if (typeof Chart === 'undefined') { return; }
            var el = document.getElementById('vas_t5vwdg_cv_' + widgetID);
            if (!el) { return; }

            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }

            var vendors = (data && data.Vendors)   ? data.Vendors   : [];
            var sym     = (data && data.CurSymbol) ? data.CurSymbol : '';
            var n       = vendors.length;
            if (!n) { return; }

            var total = 0;
            for (var ti = 0; ti < n; ti++) { total += vendors[ti].CurrAmt; }
            if (total <= 0) { return; }

            var labels  = [];
            var amounts = [];
            for (var i = 0; i < n; i++) {
                labels.push(vendors[i].Name);
                amounts.push(vendors[i].CurrAmt);
            }

            var centerText  = sym + t5vShort(total);
            var totalLabel  = VIS.Msg.getMsg('VAS_Total') || 'Total';
            var bgColors    = DONUT_COLORS.slice(0, n);

            var centerPlugin = {
                id: 'vas_t5v_center_' + widgetID,
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
                    labels: labels,
                    datasets: [{
                        data: amounts,
                        backgroundColor: bgColors,
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
                                    return labels.map(function (name, i) {
                                        var pct  = Math.round((amounts[i] / total) * 100);
                                        var trunc = name.length > 14 ? name.slice(0, 14) + '…' : name;
                                        return {
                                            text: trunc + '  ' + sym + t5vShort(amounts[i]) + '  ' + pct + '%',
                                            fillStyle:   bgColors[i],
                                            strokeStyle: bgColors[i],
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
                                    var pct = Math.round((amounts[i] / total) * 100);
                                    return ' ' + sym + t5vFmt(amounts[i], sym, data.StdPrecision) + '  (' + pct + '%)';
                                }
                            }
                        }
                    }
                },
                plugins: [centerPlugin]
            });
        }

        /* ---- Helpers ---- */
        function t5vEsc(str) {
            return String(str)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function t5vFmt(amount, sym, precision) {
            if (!amount || amount === 0) { return sym + '0'; }
            var abs   = Math.abs(amount);
            var loc   = window.navigator.language;
            var opts1 = { minimumFractionDigits: 1, maximumFractionDigits: 1 };
            var prec  = VIS.Env.getCtx().getStdPrecision() || precision || 2;
            if (abs >= 10000000) { return sym + (abs / 10000000).toLocaleString(loc, opts1) + 'Cr'; }
            if (abs >= 100000)   { return sym + (abs / 100000).toLocaleString(loc, opts1)   + 'L';  }
            if (abs >= 1000)     { return sym + (abs / 1000).toLocaleString(loc, opts1)     + 'K';  }
            return sym + abs.toLocaleString(loc, { minimumFractionDigits: prec, maximumFractionDigits: prec });
        }

        function t5vShort(val) {
            if (!val || val === 0) { return '0'; }
            var abs   = Math.abs(val);
            var loc   = window.navigator.language;
            var opts1 = { minimumFractionDigits: 1, maximumFractionDigits: 1 };
            if (abs >= 10000000) { return (abs / 10000000).toLocaleString(loc, opts1) + 'Cr'; }
            if (abs >= 100000)   { return (abs / 100000).toLocaleString(loc, opts1)   + 'L';  }
            if (abs >= 1000)     { return (abs / 1000).toLocaleString(loc, opts1)     + 'K';  }
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

        $self._drawChart = drawDonut;
    };

    /* ---- Prototype ---- */
    VAS.VAS_TopFiveVendorsWidget.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_TopFiveVendorsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_TopFiveVendorsWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
        var self = this;
        if (self._kpiData && self._drawChart) {
            window.setTimeout(function () { self._drawChart(self._kpiData); }, 100);
        }
    };

    VAS.VAS_TopFiveVendorsWidget.prototype.dispose = function () {
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
