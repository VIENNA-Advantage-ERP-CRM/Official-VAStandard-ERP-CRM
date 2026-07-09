/************************************************************
 * Module Name    : VAS
 * Purpose        : Avg Payment Period (DPO) KPI Widget
 *                  Shows average days payable outstanding, target vs gap,
 *                  trend vs last month in days, and a 7-month sparkline.
 * chronological  : Development
 * Created Date   : 13 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys used in this file (add via System Messages):
 *   VAS_016_AvgPaymentPeriodDpo  => "Avg Payment Period (DPO)"
 *   VAS_016_Target               => "Target"
 *   VAS_016_Gap                  => "Gap"
 *   VAS_016_Days                 => "days"
 *   VAS_016_DaySuffix            => "d"
 *   VAS_016_VsLastMonth          => "vs last month"
 *   VAS_016_OpenDrilldown        => "Open DPO analysis"
 *   VAS_016_DpoAnalysis          => "Days Payable Outstanding - Analysis"
 *   VAS_016_CurrentDpo           => "Current DPO"
 *   VAS_016_TargetDpo            => "Target DPO"
 *   VAS_016_LastMonthDpo         => "Last Month DPO"
 *   VAS_016_DpoByCategory        => "DPO by Category"
 *   VAS_016_DpoCurrentlyAt       => "DPO is currently"
 *   VAS_016_ExceedingTarget      => "exceeding the target of"
 *   VAS_016_WithinTarget         => "within the target of"
 *   VAS_016_By                   => "by"
 *   VAS_016_StrainNote           => "This indicates delayed payments which may strain vendor relationships."
 *   VAS_016_HealthyNote          => "Payment timing is healthy."
 *   VAS_016_WarnRunning          => "Payments are running"
 *   VAS_016_OverTarget           => "days over target — review upcoming payables to avoid strained vendor relationships."
 *   VAS_016_Loading              => "Loading..."
 *   VAS_016_NoDpoData            => "No payment history available to analyse."
 *   VAS_016_DrilldownError       => "Unable to load DPO analysis."
 *   VAS_016_Close                => "Close"
 *   VAS_016_Showing              => "Showing"
 *   VAS_016_Of                   => "of"
 *   VAS_016_PrevPage             => "Previous page"
 *   VAS_016_NextPage             => "Next page"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the title / metric / why clamps resolve against the
       dashboard's visible width, not the viewport. One document-level ResizeObserver
       serves every widget; without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. (Mirrors VIS.OutstandingSalesOrderWidget.) */
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
        var $dpoDialog = null;
        var widgetID = null;
        var drillData = null;          // cached drill-down payload

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
                url: VIS.Application.contextUrl + 'VAS/VAS_016_AvgPaymentPeriodWidget/GetAvgPaymentPeriodKpi',
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
            $container.on('click', '.vas-apwdg-open', function (e) {
                e.preventDefault();
                e.stopPropagation();
                openDpoDialog();
            });
            // The clickable overlay is a <div role="button">, so it has no native
            // keyboard activation — wire Enter / Space up manually.
            $container.on('keydown', '.vas-apwdg-open', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault();
                    e.stopPropagation();
                    openDpoDialog();
                }
            });
            $root.append($container);
        }

        /* ---- Render KPI content ---- */
        function renderKpi(data) {
            $container.empty();

            var daySuffix = msg('VAS_016_DaySuffix', 'd');
            var days = msg('VAS_016_Days', 'days');

            // Trend: difference in days vs last month (absolute, not percentage)
            var daysDiff = (data.CurrentMonthDpo || 0) - (data.LastMonthDpo || 0);
            var trendSignWord = daysDiff >= 0 ? '+' : '-';

            // Gap vs target: days over target (positive) or within target (<= 0)
            var gapDays = data.GapDays || 0;

            // WHY line: DPO vs target, then the vs-last-month movement.
            var whyText = (gapDays > 0
                ? gapDays + ' ' + days + ' ' + msg('VAS_016_OverTargetShort', 'over target')
                : msg('VAS_016_WithinTargetShort', 'within target'))
                + ' · ' + trendSignWord + Math.abs(daysDiff) + ' ' + days + ' '
                + msg('VAS_016_VsLastMonth', 'vs last month');

            // Structure mirrors AvgDaysToPayWidget: header (violet-tinted target icon +
            // title only), big metric + muted day suffix, and a WHY explanatory line.
            var html = '<div class="vas-apwdg-header">'
                +   '<div class="vas-apwdg-icon">'
                +     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"'
                +       ' stroke-linecap="round" stroke-linejoin="round">'
                +       '<circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="6"/><circle cx="12" cy="12" r="2"/>'
                +     '</svg>'
                +   '</div>'
                +   '<div class="vas-apwdg-title">' + msg('VAS_016_AvgPaymentPeriodDpo', 'Avg Payment Period (DPO)') + '</div>'
                + '</div>'
                + '<div class="vas-apwdg-value" id="vas_apwdg_val_' + widgetID + '">'
                +   '<span class="vas-apwdg-metric-val">' + (data.CurrentMonthDpo || 0) + '</span>'
                +   '<span class="vas-apwdg-metric-suffix">' + daySuffix + '</span>'
                + '</div>'
                + '<div class="vas-apwdg-why-wrap">'
                +   '<span class="vas-apwdg-why-text">' + apEsc(whyText) + '</span>'
                + '</div>'
                + '<div class="vas-apwdg-open" role="button" tabindex="0" aria-label="'
                +   apEsc(msg('VAS_016_OpenDrilldown', 'Open DPO analysis'))
                + '"></div>';

            $container.append(html);
        }

        function openDpoDialog() {
            if ($dpoDialog) {
                return;
            }

            $dpoDialog = $('<div class="vas-apwdg-dialog">');
            $('body').append($dpoDialog);

            $dpoDialog.dialog({
                autoOpen: false,
                modal: true,
                resizable: false,
                title: msg('VAS_016_DpoAnalysis', 'Days Payable Outstanding - Analysis'),
                width: Math.min(780, Math.max(320, $(window).width() - 40)),
                minHeight: 300,
                maxHeight: Math.max(320, $(window).height() - 80),
                dialogClass: 'vas-apwdg-dialog-shell',
                close: function () {
                    $dpoDialog.dialog('destroy');
                    $dpoDialog.remove();
                    $dpoDialog = null;
                    drillData = null;
                }
            });

            // Remove jQuery UI's default close button and inject our own fully-controlled one.
            var $apWidget = $dpoDialog.dialog('widget');
            $apWidget.find('.ui-dialog-titlebar-close').remove();
            var $apClose = $('<button type="button" class="vas-apwdg-dialog-close" aria-label="' + apEsc(msg('VAS_016_Close', 'Close')) + '"><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false"><path fill-rule="evenodd" clip-rule="evenodd" d="M0.331804 0.359434C0.544503 0.146971 0.832843 0.0276306 1.13348 0.0276306C1.43411 0.0276306 1.72245 0.146971 1.93515 0.359434L7.93569 6.36131L13.9376 0.359434C14.0415 0.248321 14.1667 0.159252 14.3058 0.0975303C14.4449 0.0358082 14.5949 0.00269401 14.7471 0.000157642C14.8992 -0.00237873 15.0503 0.0257147 15.1913 0.0827665C15.3324 0.139818 15.4605 0.224663 15.5681 0.332249C15.6757 0.439836 15.7605 0.567966 15.8176 0.709016C15.8746 0.850065 15.9027 1.00115 15.9002 1.15328C15.8976 1.30541 15.8645 1.45547 15.8028 1.59454C15.7411 1.73361 15.652 1.85884 15.5409 1.96278L9.53904 7.96332L15.5409 13.9652C15.742 14.1801 15.8516 14.4648 15.8467 14.759C15.8418 15.0533 15.7227 15.3341 15.5146 15.5422C15.3065 15.7504 15.0257 15.8694 14.7314 15.8743C14.4371 15.8792 14.1525 15.7696 13.9376 15.5685L7.93569 9.56667L1.93515 15.5685C1.72023 15.7696 1.43557 15.8792 1.14131 15.8743C0.847042 15.8694 0.566203 15.7504 0.358097 15.5422C0.149991 15.3341 0.0309118 15.0533 0.0260057 14.759C0.0210996 14.4648 0.130751 14.1801 0.331804 13.9652L6.33234 7.96332L0.331804 1.96278C0.119341 1.75008 0 1.46174 0 1.16111C0 0.860474 0.119341 0.572134 0.331804 0.359434Z"></path></svg></button>');
            $apClose.on('click', function () { $dpoDialog.dialog('close'); });
            $apWidget.find('.ui-dialog-titlebar').append($apClose);

            loadDpoDrilldown();
        }

        function loadDpoDrilldown() {
            // Show a busy spinner AND open the modal immediately, so clicking gives
            // instant feedback while the drill-down loads. (Previously the dialog was
            // only opened via showDpoDialog() inside the AJAX callbacks, so nothing —
            // no busy indicator — appeared during the request.)
            $dpoDialog.html('<div class="vas-apwdg-drill-state" role="status" aria-label="'
                + apEsc(msg('VAS_016_Loading', 'Loading...')) + '">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            showDpoDialog();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_016_AvgPaymentPeriodWidget/GetDpoDrilldown',
                dataType: 'json',
                async: true,
                success: function (data) {
                    var drilldown = typeof data === 'string' ? JSON.parse(data) : data;
                    renderDpoDrilldown(drilldown);
                },
                error: function () {
                    if (!$dpoDialog) { return; }
                    $dpoDialog.html('<div class="vas-apwdg-drill-state vas-apwdg-drill-error">'
                        + apEsc(msg('VAS_016_DrilldownError', 'Unable to load DPO analysis.'))
                        + '</div>');
                    showDpoDialog();
                }
            });
        }

        function renderDpoDrilldown(data) {
            if (!$dpoDialog) { return; }

            var categories = (data && data.Categories) || [];
            var current = (data && data.CurrentDpo) || 0;
            var lastMonth = (data && data.LastMonthDpo) || 0;

            if (!current && !lastMonth && !categories.length) {
                $dpoDialog.html('<div class="vas-apwdg-drill-state">'
                    + apEsc(msg('VAS_016_NoDpoData', 'No payment history available to analyse.'))
                    + '</div>');
                showDpoDialog();
                return;
            }

            drillData = data;
            paintDpoDrilldown();
        }

        /* Renders the drill-down: the three stat cards + the narrative. */
        function paintDpoDrilldown() {
            if (!$dpoDialog || !drillData) { return; }

            var data = drillData;
            var current = data.CurrentDpo || 0;
            var target = data.TargetDpo || 0;
            var lastMonth = data.LastMonthDpo || 0;
            var gap = (data.GapDays != null) ? data.GapDays : (current - target);

            var days = msg('VAS_016_Days', 'days');

            var html = '<div class="vas-apwdg-drill-stats">'
                + miniCard(msg('VAS_016_CurrentDpo', 'Current DPO'), current + ' ' + days, 'vas-apwdg-drill-mini--vio')
                + miniCard(msg('VAS_016_TargetDpo', 'Target DPO'), target + ' ' + days, 'vas-apwdg-drill-mini--grn')
                + miniCard(msg('VAS_016_LastMonthDpo', 'Last Month DPO'), lastMonth + ' ' + days, 'vas-apwdg-drill-mini--blu')
                + '</div>';

            var desc;
            if (gap > 0) {
                desc = msg('VAS_016_DpoCurrentlyAt', 'DPO is currently') + ' ' + current + ' ' + days + ', '
                    + msg('VAS_016_ExceedingTarget', 'exceeding the target of') + ' ' + target + ' ' + days + ' '
                    + msg('VAS_016_By', 'by') + ' ' + gap + ' ' + days + '. '
                    + msg('VAS_016_StrainNote', 'This indicates delayed payments which may strain vendor relationships.');
            } else {
                desc = msg('VAS_016_DpoCurrentlyAt', 'DPO is currently') + ' ' + current + ' ' + days + ', '
                    + msg('VAS_016_WithinTarget', 'within the target of') + ' ' + target + ' ' + days + '. '
                    + msg('VAS_016_HealthyNote', 'Payment timing is healthy.');
            }
            html += '<p class="vas-apwdg-drill-desc">' + apEsc(desc) + '</p>';

            if (gap > 0) {
                var warnText = msg('VAS_016_WarnRunning', 'Payments are running') + ' ' + gap + ' ' + days + ' '
                    + msg('VAS_016_OverTarget', 'days over target — review upcoming payables to avoid strained vendor relationships.');
                html += '<div class="vas-apwdg-drill-warn">' + apEsc(warnText) + '</div>';
            }

            $dpoDialog.html(html);
            showDpoDialog();
        }

        function miniCard(label, value, modClass) {
            return '<div class="vas-apwdg-drill-mini ' + modClass + '">'
                + '<div class="vas-apwdg-drill-mini-label">' + apEsc(label) + '</div>'
                + '<div class="vas-apwdg-drill-mini-value">' + apEsc(value) + '</div>'
                + '</div>';
        }

        function msg(key, fallback) {
            var value = VIS.Msg.getMsg(key);
            return value && value !== key && value !== '[' + key + ']' ? value : fallback;
        }

        function showDpoDialog() {
            if (!$dpoDialog) { return; }
            $dpoDialog.dialog('open');
            $dpoDialog.dialog('option', 'position', { my: 'center', at: 'center', of: window });
        }

        function apEsc(value) {
            return $('<div>').text(value == null ? '' : value).html();
        }

        this.closeDpoDialog = function () {
            if ($dpoDialog) {
                if ($dpoDialog.dialog('isOpen')) {
                    $dpoDialog.dialog('close');
                } else {
                    $dpoDialog.dialog('destroy');
                    $dpoDialog.remove();
                    $dpoDialog = null;
                    drillData = null;
                }
            }
        };

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
        if (this.closeDpoDialog) {
            this.closeDpoDialog();
        }
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
