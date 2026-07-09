/************************************************************
 * Module Name    : VAS
 * Purpose        : Top Five Vendors by Spend Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_023_Top5VendorsBySpend => "Top 5 Vendors by Spend"
 *   VAS_023_VendorSpendDist    => "Vendor Spend Distribution"
 *   VAS_023_Total              => "Total"
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

    VAS.VAS_023_TopFiveVendorsWidget = function () {
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
                url: VIS.Application.contextUrl + 'VAS/VAS_023_TopFiveVendorsWidget/GetTopFiveVendors',
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

            var total = 0;
            for (var ti = 0; ti < vendors.length; ti++) { total += (vendors[ti].CurrAmt || 0); }

            var html =
                '<div class="vas-t5vwdg-header">' +
                    '<div class="vas-t5vwdg-title">' +
                        '<svg class="vas-t5vwdg-title-icon" viewBox="0 0 24 24" fill="none"' +
                            ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                            '<rect x="2" y="7" width="20" height="14" rx="2" ry="2"/>' +
                            '<path d="M16 7V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2"/>' +
                        '</svg>' +
                        t5vEsc(msg('VAS_023_Top5VendorsBySpend', 'Top 5 Vendors by Spend')) +
                    '</div>' +
                '</div>' +
                '<div class="vas-t5vwdg-list">';

            for (var i = 0; i < vendors.length; i++) {
                var v   = vendors[i];
                var rankCls = 'vas-t5vwdg-avatar--r' + Math.min(v.Rank, 5);
                html +=
                    '<div class="vas-t5vwdg-row">' +
                        '<span class="vas-t5vwdg-avatar ' + rankCls + '">' +
                            t5vEsc(v.Initials) +
                        '</span>' +
                        '<div class="vas-t5vwdg-info">' +
                            '<div class="vas-t5vwdg-name" title="' + t5vEsc(v.Name)     + '">' + t5vEsc(v.Name)     + '</div>' +
                            '<div class="vas-t5vwdg-cat" title="'  + t5vEsc(v.Category) + '">' + t5vEsc(v.Category) + '</div>' +
                        '</div>' +
                        '<div class="vas-t5vwdg-metrics">' +
                            '<div class="vas-t5vwdg-amt">' + t5vFmt(v.CurrAmt, sym, data.StdPrecision) + '</div>' +
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
                    t5vEsc(msg('VAS_023_VendorSpendDist', 'Vendor Spend Distribution')) +
                '</div>' +
                '<div class="vas-t5vwdg-chart-wrap">' +
                    '<div class="vas-t5vwdg-donut">' +
                        '<canvas class="vas-t5vwdg-canvas" id="vas_t5vwdg_cv_' + widgetID + '"></canvas>' +
                    '</div>' +
                    '<div class="vas-t5vwdg-legend">' +
                        buildLegend(vendors, total, sym, data.StdPrecision) +
                    '</div>' +
                '</div>';

            $container.html(html);
            window.setTimeout(function () { drawDonut(data); }, 80);
        }

        /* ---- Custom HTML legend (full CSS control: name is regular weight and
             ellipsis-truncated so it never clips off the widget edge). ---- */
        function buildLegend(vendors, total, sym, precision) {
            var out = '';
            for (var i = 0; i < vendors.length; i++) {
                var v     = vendors[i];
                var pct   = total > 0 ? Math.round((v.CurrAmt / total) * 100) : 0;
                var color = DONUT_COLORS[i % DONUT_COLORS.length];
                out +=
                    '<div class="vas-t5vwdg-lg-row">' +
                        '<span class="vas-t5vwdg-lg-dot" style="background:' + color + ';"></span>' +
                        '<span class="vas-t5vwdg-lg-name" title="' + t5vEsc(v.Name) + '">' + t5vEsc(v.Name) + '</span>' +
                        '<span class="vas-t5vwdg-lg-val">' + t5vEsc(sym + t5vShort(v.CurrAmt) + ' · ' + pct + '%') + '</span>' +
                    '</div>';
            }
            return out;
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
            var totalLabel  = msg('VAS_023_Total', 'Total');
            var bgColors    = DONUT_COLORS.slice(0, n);

            var centerPlugin = {
                id: 'VAS_023_t5v_center_' + widgetID,
                afterDraw: function (chart) {
                    var ctx = chart.ctx;
                    var cx  = (chart.chartArea.left + chart.chartArea.right)  / 2;
                    var cy  = (chart.chartArea.top  + chart.chartArea.bottom) / 2;
                    /* Scale with the rendered donut size so the center text keeps its
                       proportion on zoom/resize (fs = "Total" label; amount is larger). */
                    var fs    = Math.max(10, Math.min(16, chart.width / 12));
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
                        legend: { display: false },
                        tooltip: {
                            callbacks: {
                                label: function (ctx) {
                                    var i   = ctx.dataIndex;
                                    var pct = Math.round((amounts[i] / total) * 100);
                                    return ' ' + t5vFmt(amounts[i], sym, data.StdPrecision) + '  (' + pct + '%)';
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

        /* Compact magnitude via the shared CurrencyFormat util (Indian vs international
           per base-currency ISO), exactly like VAS_019/VAS_024. Returns the unsigned
           magnitude only — callers prepend the sign/symbol. */
        function t5vShort(val) {
            var d = $self._kpiData || {};
            return VIS.Util.formatCompactAmount(val, d.CurIso || '', d.StdPrecision);
        }

        function t5vFmt(amount, sym, precision) {
            amount = Number(amount || 0);
            var sign = amount < 0 ? '-' : '';
            return sign + sym + t5vShort(amount);
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
    VAS.VAS_023_TopFiveVendorsWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_023_TopFiveVendorsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_023_TopFiveVendorsWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
        var self = this;
        if (self._kpiData && self._drawChart) {
            window.setTimeout(function () { self._drawChart(self._kpiData); }, 100);
        }
    };

    VAS.VAS_023_TopFiveVendorsWidget.prototype.dispose = function () {
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
