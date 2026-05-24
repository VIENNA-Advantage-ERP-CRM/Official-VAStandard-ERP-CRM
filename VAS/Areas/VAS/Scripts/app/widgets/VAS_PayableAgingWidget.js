/************************************************************
 * Module Name    : VAS
 * Purpose        : Payable Aging + Payment Health KPI Widget
 * chronological  : Development
 * Created Date   : 13 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_PayableAging     => "Payable Aging"
 *   VAS_PaymentHealth    => "Payment Health"
 *   VAS_Overdue          => "overdue"
 *   VAS_Aging0to30       => "0–30d"
 *   VAS_Aging31to60      => "31–60d"
 *   VAS_Aging61to90      => "61–90d"
 *   VAS_Aging90Plus      => "90+d"
 *   VAS_PaidOnTime       => "Paid on time"
 *   VAS_Paid             => "Paid"
 *   VAS_Pending          => "Pending"
 *   VAS_OverdueLabel     => "Overdue"
 *   VAS_InvSuffix        => "inv."
 *   VAS_Crore            => "Cr"
 *   VAS_Lakh             => "L"
 *   VAS_Thousand         => "K"
 *   VAS_Million          => "M"
 *   VAS_Billion          => "B"
 *   VAS_Trillion         => "T"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_PayableAgingWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self     = this;
        var $root     = $('<div class="h-100 w-100 vas-widget-bg vas-pawdg-root">');
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
                url: VIS.Application.contextUrl + 'VAS/VAS_PayableAgingWidget/GetPayableAgingKpi',
                dataType: 'json',
                async: true,
                success: function (data) {
                    $self._kpiData = typeof data === 'string' ? JSON.parse(data) : data;
                    if ($self._kpiData) { renderKpi($self._kpiData); }
                    $bsyDiv[0].style.visibility = 'hidden';
                },
                error: function () {
                    $bsyDiv[0].style.visibility = 'hidden';
                }
            });
        };

        /* ---- Build root shell ---- */
        function buildShell() {
            $container = $('<div class="vas-pawdg-container" id="vas_pawdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        /* ---- Render KPI content ---- */
        function renderKpi(data) {
            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }
            $container.empty();

            var sym       = data.CurSymbol || '';
            var totalOpen = data.Bucket0to30 + data.Bucket31to60 + data.Bucket61to90 + data.Bucket90Plus;
            var totalAll  = data.PaidAmount  + data.PendingAmount + data.OverdueAmount;

            var paidPct    = totalAll > 0 ? Math.round((data.PaidAmount    / totalAll) * 100) : 0;
            var pendingPct = totalAll > 0 ? Math.round((data.PendingAmount / totalAll) * 100) : 0;
            var overduePct = totalAll > 0 ? Math.round((data.OverdueAmount / totalAll) * 100) : 0;

            var html = ''
                /* ---- Header ---- */
                + '<div class="vas-pawdg-header">'
                +   '<div class="vas-pawdg-title">'
                +     '<svg class="vas-pawdg-title-icon" viewBox="0 0 24 24" fill="none"'
                +         ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                +       '<circle cx="12" cy="12" r="10"/>'
                +       '<polyline points="12 6 12 12 16 14"/>'
                +     '</svg>'
                +     VIS.Msg.getMsg('VAS_PayableAging')
                +   '</div>'
                +   '<span class="vas-pawdg-chip">' + data.OverdueCount + ' ' + VIS.Msg.getMsg('VAS_Overdue') + '</span>'
                + '</div>'

                /* ---- Aging bars ---- */
                + '<div class="vas-pawdg-aging-bars">'
                +   buildAgingRow('VAS_Aging0to30',  data.Bucket0to30,  data.Count0to30,  totalOpen, sym, data.StdPrecision, 'vas-pawdg-ag-fill--green')
                +   buildAgingRow('VAS_Aging31to60', data.Bucket31to60, data.Count31to60, totalOpen, sym, data.StdPrecision, 'vas-pawdg-ag-fill--amber')
                +   buildAgingRow('VAS_Aging61to90', data.Bucket61to90, data.Count61to90, totalOpen, sym, data.StdPrecision, 'vas-pawdg-ag-fill--orange')
                +   buildAgingRow('VAS_Aging90Plus', data.Bucket90Plus, data.Count90Plus, totalOpen, sym, data.StdPrecision, 'vas-pawdg-ag-fill--red')
                + '</div>'

                /* ---- Divider ---- */
                + '<div class="vas-pawdg-divider"></div>'

                /* ---- Payment Health header ---- */
                + '<div class="vas-pawdg-section-header">'
                +   '<svg class="vas-pawdg-title-icon" viewBox="0 0 24 24" fill="none"'
                +       ' stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                +     '<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>'
                +   '</svg>'
                +   VIS.Msg.getMsg('VAS_PaymentHealth')
                + '</div>'

                /* ---- Donut (Chart.js) ---- */
                + '<div class="vas-pawdg-health-row">'
                +   '<div class="vas-pawdg-donut-wrap">'
                +     '<canvas class="vas-pawdg-donut-canvas" id="vas_pawdg_donut_' + widgetID + '"></canvas>'
                +   '</div>'
                + '</div>'

                /* ---- Legend ---- */
                + '<div class="vas-pawdg-legend">'
                +   buildLegendRow('vas-pawdg-dot--ok',   VIS.Msg.getMsg('VAS_PaidOnTime'),   sym + formatAmount(data.PaidAmount,    data.StdPrecision) + ' · ' + paidPct    + '%', 'vas-pawdg-lv-ok')
                +   buildLegendRow('vas-pawdg-dot--info', VIS.Msg.getMsg('VAS_Pending'),       sym + formatAmount(data.PendingAmount, data.StdPrecision) + ' · ' + pendingPct + '%', 'vas-pawdg-lv-info')
                +   buildLegendRow('vas-pawdg-dot--err',  VIS.Msg.getMsg('VAS_OverdueLabel'),  sym + formatAmount(data.OverdueAmount, data.StdPrecision) + ' · ' + overduePct + '%', 'vas-pawdg-lv-err')
                + '</div>';

            $container.html(html);

            /* Animate aging bar fills from 0 → target width */
            window.setTimeout(function () {
                $container.find('.vas-pawdg-ag-fill').each(function () {
                    $(this).css('width', $(this).data('w') + '%');
                });
            }, 60);

            /* Draw Chart.js donut */
            window.setTimeout(function () { drawDonut(data, paidPct); }, 80);
        }

        /* ---- Chart.js Donut (Payment Health) ---- */
        function drawDonut(data, paidPct) {
            if (typeof Chart === 'undefined') { return; }
            var el = document.getElementById('vas_pawdg_donut_' + widgetID);
            if (!el) { return; }

            if (_chartInst) { _chartInst.destroy(); _chartInst = null; }

            var sym      = data.CurSymbol || '';
            var totalAll = data.PaidAmount + data.PendingAmount + data.OverdueAmount;
            if (totalAll <= 0) { return; }

            var pct = (paidPct !== undefined) ? paidPct
                    : Math.round((data.PaidAmount / totalAll) * 100);

            var centerPlugin = {
                id: 'vas_pawdg_center_' + widgetID,
                afterDraw: function (chart) {
                    var ctx = chart.ctx;
                    var cx  = (chart.chartArea.left + chart.chartArea.right) / 2;
                    var cy  = (chart.chartArea.top  + chart.chartArea.bottom) / 2;
                    var fs  = Math.max(9, Math.min(12, chart.width / 110));
                    ctx.save();
                    ctx.textAlign    = 'center';
                    ctx.textBaseline = 'middle';
                    ctx.fillStyle    = '#102C3F';
                    ctx.font         = 'bold ' + (fs + 2) + 'px Roboto, sans-serif';
                    ctx.fillText(pct + '%', cx, cy - fs * 0.6);
                    ctx.font      = fs + 'px Roboto, sans-serif';
                    ctx.fillStyle = '#748494';
                    ctx.fillText(VIS.Msg.getMsg('VAS_Paid') || 'Paid', cx, cy + fs * 0.9);
                    ctx.restore();
                }
            };

            _chartInst = new Chart(el, {
                type: 'doughnut',
                data: {
                    labels: [
                        VIS.Msg.getMsg('VAS_PaidOnTime')   || 'Paid on time',
                        VIS.Msg.getMsg('VAS_Pending')      || 'Pending',
                        VIS.Msg.getMsg('VAS_OverdueLabel') || 'Overdue'
                    ],
                    datasets: [{
                        data: [data.PaidAmount, data.PendingAmount, data.OverdueAmount],
                        backgroundColor: ['#20A464', '#0083DA', '#D14545'],
                        borderWidth: 2,
                        borderColor: 'rgba(255,255,255,0.8)',
                        hoverBorderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: '60%',
                    layout: { padding: 4 },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            callbacks: {
                                label: function (ctx) {
                                    var p = Math.round((ctx.raw / totalAll) * 100);
                                    return ' ' + ctx.label + ': ' + sym + formatAmount(ctx.raw, data.StdPrecision) + '  (' + p + '%)';
                                }
                            }
                        }
                    }
                },
                plugins: [centerPlugin]
            });
        }

        /* ---- Build one aging row (label | bar | amount | count) ---- */
        function buildAgingRow(msgKey, amount, count, totalOpen, sym, stdPrecision, colorClass) {
            var pct    = totalOpen > 0 ? Math.round((amount / totalOpen) * 100) : 0;
            var amtFmt = sym + formatAmount(amount, stdPrecision);
            var cntFmt = count + ' ' + VIS.Msg.getMsg('VAS_InvSuffix');
            return '<div class="vas-pawdg-aging-row">'
                + '<div class="vas-pawdg-ag-lbl">' + VIS.Msg.getMsg(msgKey) + '</div>'
                + '<div class="vas-pawdg-ag-track">'
                +   '<div class="vas-pawdg-ag-fill ' + colorClass + '" data-w="' + pct + '"></div>'
                + '</div>'
                + '<div class="vas-pawdg-ag-val">' + amtFmt + '</div>'
                + '<div class="vas-pawdg-ag-cnt">' + cntFmt + '</div>'
                + '</div>';
        }

        /* ---- Build one legend row ---- */
        function buildLegendRow(dotClass, label, value, valueClass) {
            return '<div class="vas-pawdg-legend-row">'
                +   '<span class="vas-pawdg-legend-lhs">'
                +     '<span class="vas-pawdg-legend-dot ' + dotClass + '"></span>'
                +     '<span class="vas-pawdg-legend-label">' + label + '</span>'
                +   '</span>'
                +   '<span class="vas-pawdg-legend-value ' + valueClass + '">' + value + '</span>'
                + '</div>';
        }

        /* ---- Number formatter ---- */
        function formatAmount(number, stdPrecision) {
            var prec      = VIS.Env.getCtx().getStdPrecision() || stdPrecision || 2;
            var isNegative = number < 0;
            var absNumber  = Math.abs(number);
            var formatted, unit = '';
            var opts2   = { minimumFractionDigits: 2, maximumFractionDigits: 2 };
            var optsRaw = { minimumFractionDigits: prec, maximumFractionDigits: prec };
            if (absNumber >= 1000000000000) {
                unit      = VIS.Msg.getMsg('VAS_Trillion');
                formatted = (absNumber / 1000000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000000000) {
                unit      = VIS.Msg.getMsg('VAS_Billion');
                formatted = (absNumber / 1000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 10000000) {
                unit      = VIS.Msg.getMsg('VAS_Crore');
                formatted = (absNumber / 10000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 100000) {
                unit      = VIS.Msg.getMsg('VAS_Lakh');
                formatted = (absNumber / 100000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000) {
                unit      = VIS.Msg.getMsg('VAS_Thousand');
                formatted = (absNumber / 1000).toLocaleString(window.navigator.language, opts2);
            } else {
                formatted = absNumber.toLocaleString(window.navigator.language, optsRaw);
            }
            return (isNegative ? '-' : '') + formatted + unit;
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

        $self._drawChart = function (data) {
            if (!data) { return; }
            var totalAll = data.PaidAmount + data.PendingAmount + data.OverdueAmount;
            var paidPct  = totalAll > 0 ? Math.round((data.PaidAmount / totalAll) * 100) : 0;
            drawDonut(data, paidPct);
        };
    };

    /* ---- Prototype ---- */
    VAS.VAS_PayableAgingWidget.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_PayableAgingWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_PayableAgingWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
        var self = this;
        if (self._kpiData && self._drawChart) {
            window.setTimeout(function () { self._drawChart(self._kpiData); }, 100);
        }
    };

    VAS.VAS_PayableAgingWidget.prototype.dispose = function () {
        if (this.frame) { this.frame.dispose(); }
        this.frame    = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
