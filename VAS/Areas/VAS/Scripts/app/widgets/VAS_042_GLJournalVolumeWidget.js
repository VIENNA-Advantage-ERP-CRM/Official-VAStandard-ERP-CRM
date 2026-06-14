/**
 * GL Journal Volume by Day Chart Widget
 * Purpose  : Bar chart of GL Journal count per day with a net-value trend
 *            line overlay. Bars are colour-coded: Posted, Weekend, This week.
 *            Supports a Week / Month period toggle.
 * Tables   : GL_Journal, GL_JournalLine, C_AcctSchema, C_Currency
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_042_JournalVolumeByDay, VAS_042_CountVsValue,
    //            VAS_042_Week, VAS_042_Month,
    //            VAS_042_Posted, VAS_042_Weekend, VAS_042_ThisWeek, VAS_042_NetValueTrend
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function esc(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function fmtAmt(amount, precision) {
        var stdPrecision = VIS.Env.getCtx().getStdPrecision();
        var prec = (typeof precision === 'number' && precision >= 0) ? precision : stdPrecision;
        return parseFloat(Math.abs(amount) || 0).toLocaleString(window.navigator.language, {
            minimumFractionDigits: prec,
            maximumFractionDigits: prec
        });
    }

    function fmtMoney(amount, currency, precision) {
        var val = parseFloat(amount || 0);
        return (val < 0 ? '-' : '') + (currency || '') + fmtAmt(val, precision);
    }

    // ── SVG chart constants ───────────────────────────────────────────────────
    var SVG_W  = 720, SVG_H  = 200;
    var ML     = 26,  MR     = 6,   MT = 12, MB = 22;   // margins
    var CHART_W = SVG_W - ML - MR;                        // 688
    var CHART_H = SVG_H - MT - MB;                        // 166

    // Bar colours
    var C_POSTED  = '#0083DA';
    var C_WEEK    = '#1F83FF';
    var C_TREND   = '#019D89';
    var C_TREND_W = '#0B6B45';  // trend dot for this-week days

    // ── Round up to a "nice" Y-axis maximum ──────────────────────────────────
    function niceMax(val) {
        var steps = [4, 6, 8, 10, 12, 15, 20, 25, 30, 40, 50, 75, 100];
        for (var i = 0; i < steps.length; i++) {
            if (steps[i] >= val) { return steps[i]; }
        }
        return Math.ceil(val / 10) * 10;
    }

    // ── Build the entire SVG string from the day array ────────────────────────
    function buildSVG(days, currency, precision) {
        var n = days.length;

        // No data guard
        if (!n) {
            return '<svg viewBox="0 0 ' + SVG_W + ' ' + SVG_H + '"'
                + ' width="100%" height="100%">'
                + '<text x="' + (SVG_W / 2) + '" y="' + (SVG_H / 2) + '"'
                + ' text-anchor="middle" font-size="11" fill="#748494">'
                + lbl('VIS_NoData', 'No data available.') + '</text></svg>';
        }

        // Find max count and max |NetValue|
        var maxCount  = 0;
        var maxNetVal = 0;
        for (var i = 0; i < n; i++) {
            if (days[i].JournalCount > maxCount)  { maxCount  = days[i].JournalCount; }
            if (Math.abs(days[i].NetValue) > maxNetVal) { maxNetVal = Math.abs(days[i].NetValue); }
        }
        if (maxCount  === 0) { maxCount  = 1; }
        if (maxNetVal === 0) { maxNetVal = 1; }

        var yMax   = niceMax(maxCount);
        var slotW  = CHART_W / n;
        var barW   = Math.max(3, Math.min(slotW * 0.62, 42));

        var parts = [];

        // ── Grid lines + Y-axis labels (4 levels) ────────────────────────────
        parts.push('<g>');
        for (var t = 1; t <= 4; t++) {
            var ratio = t / 4;
            var gy    = MT + CHART_H * (1 - ratio);
            var yLbl  = Math.round(yMax * ratio);
            parts.push('<line x1="' + ML + '" y1="' + gy.toFixed(1)
                + '" x2="' + (SVG_W - MR) + '" y2="' + gy.toFixed(1)
                + '" stroke="#E4EDF4" stroke-width="1"/>');
            parts.push('<text x="' + (ML - 3) + '" y="' + (gy + 3).toFixed(1)
                + '" font-size="8" fill="#748494" text-anchor="end">' + yLbl + '</text>');
        }
        parts.push('</g>');

        // ── Bars ──────────────────────────────────────────────────────────────
        var trendPts = [];
        parts.push('<g>');
        for (var i = 0; i < n; i++) {
            var d     = days[i];
            var slotX = ML + i * slotW;
            var barX  = slotX + (slotW - barW) / 2;
            var bh    = Math.max(2, (d.JournalCount / yMax) * CHART_H);
            var by    = MT + CHART_H - bh;

            var fill, opacity;
            if (d.IsWeekend) {
                fill = C_POSTED; opacity = '0.4';
            } else if (d.IsThisWeek) {
                fill = C_WEEK;   opacity = '1';
            } else {
                fill = C_POSTED; opacity = '0.85';
            }

            var info = (d.DateStr || d.DayNum)
                + ' - ' + d.JournalCount + ' ' + lbl('VAS_041_GLJEntries', 'Entries')
                + ', ' + lbl('VAS_042_NetValueTrend', 'Net value trend')
                + ': ' + fmtMoney(d.NetValue, currency, precision);

            parts.push('<rect class="VAS-gljv-bar" x="' + barX.toFixed(1) + '" y="' + by.toFixed(1)
                + '" width="' + barW.toFixed(1) + '" height="' + bh.toFixed(1)
                + '" rx="3" fill="' + fill + '" opacity="' + opacity + '">'
                + '<title>' + esc(info) + '</title></rect>');

            // Trend point — scaled independently to 85 % of chart height
            var tx = slotX + slotW / 2;
            var ty = MT + CHART_H - (Math.abs(d.NetValue) / maxNetVal) * CHART_H * 0.85;
            trendPts.push({ x: tx.toFixed(1), y: ty.toFixed(1), isWeek: d.IsThisWeek, info: info });
        }
        parts.push('</g>');

        // ── Trend polyline ────────────────────────────────────────────────────
        var ptStr = trendPts.map(function (p) { return p.x + ',' + p.y; }).join(' ');
        parts.push('<polyline points="' + ptStr + '" fill="none" stroke="' + C_TREND
            + '" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" opacity="0.9"/>');

        // ── Trend dots ────────────────────────────────────────────────────────
        for (var i = 0; i < trendPts.length; i++) {
            var p = trendPts[i];
            parts.push('<circle class="VAS-gljv-dot" cx="' + p.x + '" cy="' + p.y + '" r="2.5"'
                + ' fill="' + (p.isWeek ? C_TREND_W : C_TREND) + '">'
                + '<title>' + esc(p.info) + '</title></circle>');
        }

        // ── X-axis labels (every day for week; every 2 days for month) ────────
        var interval = n > 10 ? 2 : 1;
        for (var i = 0; i < n; i++) {
            if (i % interval !== 0) { continue; }
            var lx = ML + i * slotW + slotW / 2;
            parts.push('<text x="' + lx.toFixed(1) + '" y="' + (SVG_H - 4)
                + '" font-size="8" fill="#748494" text-anchor="middle">'
                + days[i].DayNum + '</text>');
        }

        return '<svg viewBox="0 0 ' + SVG_W + ' ' + SVG_H + '"'
            + ' preserveAspectRatio="none" width="100%" height="100%">'
            + parts.join('') + '</svg>';
    }

    // ──────────────────────────────────────────────────────────────────────────
    VAS.VAS_042_GLJournalVolumeWidget = function () {

        this.frame;
        this.windowNo;
        var $self        = this;
        var $root        = $('<div class="VAS-gljv-root">');
        var activePeriod = 'month';
        var baseUrl      = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-gljv-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljv-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            // Bar-chart SVG icon
            var barIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<line x1="18" y1="20" x2="18" y2="10"></line>'
                + '<line x1="12" y1="20" x2="12" y2="4"></line>'
                + '<line x1="6" y1="20" x2="6" y2="14"></line>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="VAS-gljv-card">'

                // ── Header ────────────────────────────────────────────────────
                + '<div class="w-head">'
                +   '<div class="VAS-gljv-icon">' + barIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_042_JournalVolumeByDay', 'Journal Volume by Day') + '</div>'
                +   '<span class="VAS-gljv-sub" id="VAS-gljv-sub-' + id + '">—</span>'
                +   '<button class="VAS-gljv-toggle" id="VAS-gljv-toggle-' + id + '">'
                +     lbl('VAS_042_Month', 'Month') + ' ▾'
                +   '</button>'
                + '</div>'

                // ── Chart area ────────────────────────────────────────────────
                + '<div class="VAS-gljv-chart-wrap" id="VAS-gljv-chart-' + id + '"></div>'

                // ── Legend ────────────────────────────────────────────────────
                + '<div class="VAS-gljv-legend">'
                +   '<div class="VAS-gljv-leg-item">'
                +     '<span class="VAS-gljv-swatch VAS-gljv-sw-posted"></span>'
                +     lbl('VAS_042_Posted', 'Posted')
                +   '</div>'
                +   '<div class="VAS-gljv-leg-item">'
                +     '<span class="VAS-gljv-swatch VAS-gljv-sw-weekend"></span>'
                +     lbl('VAS_042_Weekend', 'Weekend')
                +   '</div>'
                +   '<div class="VAS-gljv-leg-item">'
                +     '<span class="VAS-gljv-swatch VAS-gljv-sw-week"></span>'
                +     lbl('VAS_042_ThisWeek', 'This week')
                +   '</div>'
                +   '<div class="VAS-gljv-leg-item">'
                +     '<span class="VAS-gljv-swatch VAS-gljv-sw-trend"></span>'
                +     lbl('VAS_042_NetValueTrend', 'Net value trend')
                +   '</div>'
                + '</div>'

                + '</div>'; // .VAS-gljv-card

            $root.append(html);

            // Period toggle
            $root.find('#VAS-gljv-toggle-' + id).on('click', function () {
                activePeriod = (activePeriod === 'month') ? 'week' : 'month';
                $(this).text(
                    lbl(activePeriod === 'month' ? 'VAS_042_Month' : 'VAS_042_Week',
                        activePeriod === 'month' ? 'Month' : 'Week') + ' ▾'
                );
                showBusy(true);
                loadData();
            });
        }

        function loadData() {
            $.ajax({
                url      : baseUrl + 'VAS/VAS_042_GLJournalVolumeWidget/GetVolumeByDay',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                data     : { period: activePeriod },
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data && data.Days) {
                            var id = $self.AD_UserHomeWidgetID;
                            // Update sub-label: "APR 1 – 18 · Count vs Value"
                            $root.find('#VAS-gljv-sub-' + id).text(
                                (data.PeriodLabel || '').toUpperCase()
                                + ' · '
                                + lbl('VAS_042_CountVsValue', 'Count vs Value')
                            );
                            // Inject SVG
                            $root.find('#VAS-gljv-chart-' + id).html(
                                buildSVG(data.Days, data.CurSymbol || data.ISOCode || '', data.StdPrecision)
                            );
                        } else {
                            showError();
                        }
                    } catch (e) {
                        showError();
                    }
                    showBusy(false);
                },
                error: function () {
                    showError();
                    showBusy(false);
                }
            });
        }

        function showError() {
            var id = $self.AD_UserHomeWidgetID;
            $root.find('#VAS-gljv-chart-' + id).html(
                '<svg viewBox="0 0 720 200" width="100%" height="100%">'
                + '<text x="360" y="100" text-anchor="middle" font-size="11" fill="#748494">'
                + lbl('VIS_Error', 'Error loading data.') + '</text></svg>'
            );
        }

        this.refreshWidget    = function () { showBusy(true); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () { $root.remove(); };
    };

    VAS.VAS_042_GLJournalVolumeWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_042_GLJournalVolumeWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_042_GLJournalVolumeWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_042_GLJournalVolumeWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
