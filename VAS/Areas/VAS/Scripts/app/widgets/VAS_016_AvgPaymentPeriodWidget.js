/************************************************************
 * Module Name    : VAS
 * Purpose        : AP "Avg Days to Pay" KPI Widget — the payables analog of
 *                  VIS.AvgDaysToPayWidget (which is the AR/customer side). Shows the
 *                  amount-weighted average number of days WE take to pay suppliers this
 *                  quarter, with a "N days faster/slower than last quarter" subtitle.
 * chronological  : Development
 * Created Date   : 13 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys used in this file (add via System Messages):
 *   VAS_016_AvgDaysToPay            => "Avg Days to Pay"
 *   VAS_056_DaysToPayThisQuarter    => "Days to Pay (This Quarter)"
 *   VAS_016_DaySuffix               => "d"
 *   VAS_DaysFasterThanLastQuarter   => " days faster than last quarter"
 *   VAS_DaysSlowerThanLastQuarter   => " days slower than last quarter"
 *   VIS_NoChange                    => "No change"
 *   VAS_016_Loading                 => "Loading..."
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the title / metric clamps resolve against the dashboard's
       visible width, not the viewport. One document-level ResizeObserver serves
       every widget; without a marked container — or without ResizeObserver — the
       CSS falls back to 100vw. (Mirrors VIS.AvgDaysToPayWidget.) */
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

    VAS.VAS_016_AvgPaymentPeriodWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-apwdg-root">');
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

        /* ---- Data load: AP-side avg days to pay (quarter vs last quarter) ---- */
        this.intialLoad = function () {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_016_AvgPaymentPeriodWidget/GetAvgDaysToPay',
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

        /* Comparison line vs the previous quarter, mirroring VIS.AvgDaysToPayWidget's subtitle.
           diff = currentAvgDays - previousAvgDays: negative = we paid faster (good), positive = slower. */
        function comparisonText(diff) {
            var d = diff || 0;
            if (d < 0) { return Math.abs(d) + msg('VAS_DaysFasterThanLastQuarter', ' days faster than last quarter'); }
            if (d > 0) { return d + msg('VAS_DaysSlowerThanLastQuarter', ' days slower than last quarter'); }
            return msg('VIS_NoChange', 'No change');
        }

        /* ---- Render KPI content ---- */
        function renderKpi(data) {
            $container.empty();

            var daySuffix = msg('VAS_016_DaySuffix', 'd');
            var days = data.currentAvgDays || 0;
            var subtitle = comparisonText(data.differenceDays);

            // Structure mirrors AvgDaysToPayWidget: header (target icon + title), big metric +
            // muted day suffix, and a comparison-vs-last-quarter subtitle line.
            var html = '<div class="vas-apwdg-header">'
                +   '<div class="vas-apwdg-icon">'
                +     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"'
                +       ' stroke-linecap="round" stroke-linejoin="round">'
                +       '<circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="6"/><circle cx="12" cy="12" r="2"/>'
                +     '</svg>'
                +   '</div>'
                +   '<div class="vas-apwdg-title">' + msg('VAS_016_AvgDaysToPay', 'Avg Days to Pay') + '</div>'
                + '</div>'
                + '<div class="vas-apwdg-value" id="vas_apwdg_val_' + widgetID + '">'
                +   '<span class="vas-apwdg-metric-val">' + days + '</span>'
                +   '<span class="vas-apwdg-metric-suffix">' + daySuffix + '</span>'
                + '</div>'
                + '<div class="vas-apwdg-why-wrap">'
                +   '<span class="vas-apwdg-why-text">' + apEsc(subtitle) + '</span>'
                + '</div>';

            $container.append(html);
        }

        function msg(key, fallback) {
            var value = VIS.Msg.getMsg(key);
            return value && value !== key && value !== '[' + key + ']' ? value : fallback;
        }

        function apEsc(value) {
            return $('<div>').text(value == null ? '' : value).html();
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
    VAS.VAS_016_AvgPaymentPeriodWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        // Self-wire the dashboard-width CSS variable (--dash-inline-size) the clamps read.
        ensureDashInlineSizeVar(this.getRoot());
        var self = this;
        window.setTimeout(function () {
            self.intialLoad();
        }, 50);
    };

    VAS.VAS_016_AvgPaymentPeriodWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_016_AvgPaymentPeriodWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_016_AvgPaymentPeriodWidget.prototype.dispose = function () {
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
