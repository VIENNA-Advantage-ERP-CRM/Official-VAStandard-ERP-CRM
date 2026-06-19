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
 *   VAS_024_TotalOutstanding  => "Total Outstanding"
 *   VAS_024_Invoices          => "Invoices"
 *   VAS_024_Vendors           => "Vendors"
 *   VAS_024_Worsening         => "worsening"
 *   VAS_024_Improving         => "improving"
 *   VAS_024_Crore             => "Cr"
 *   VAS_024_Lakh              => "L"
 *   VAS_024_Thousand          => "K"
 *   VAS_024_Million           => "M"
 *   VAS_024_Billion           => "B"
 *   VAS_024_Trillion          => "T"
 *   VAS_024_OpenDrilldown     => "Open outstanding payables breakdown"
 *   VAS_024_OutstandingBreakdown => "Outstanding Payables - Drill-down"
 *   VAS_024_OutstandingPayables => "Outstanding Payables"
 *   VAS_024_OverdueExposure   => "Overdue Exposure"
 *   VAS_024_OverdueInvoices   => "Overdue Invoices"
 *   VAS_024_TopVendors        => "Top Vendors"
 *   VAS_024_RemainUnpaid      => "remain unpaid across"
 *   VAS_024_InvoicesLower     => "invoices"
 *   VAS_024_VendorsLower      => "vendors"
 *   VAS_024_OverdueAttention  => "Immediate attention required for overdue invoices."
 *   VAS_024_Share             => "Share"
 *   VAS_024_MaxDpd            => "Max DPD"
 *   VAS_024_Days              => "d"
 *   VAS_024_Loading           => "Loading..."
 *   VAS_024_NoOutstandingData => "No outstanding purchase payables found."
 *   VAS_024_DrilldownError    => "Unable to load outstanding payables details."
 *   VAS_024_Close             => "Close"
 *   VAS_024_Showing           => "Showing"
 *   VAS_024_Of                => "of"
 *   VAS_024_PrevPage          => "Previous page"
 *   VAS_024_NextPage          => "Next page"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_024_TotalOutstandingWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-towdg-root">');
        var $container;
        var $outstandingDialog = null;
        var widgetID = null;
        var drillData = null;          // cached drill-down payload for client-side paging
        var drillPage = 0;             // current 0-based page in the drill-down popup
        var DRILL_PAGE_SIZE = 6;       // vendor rows shown per page (rule 18 - paging)

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
                url: VIS.Application.contextUrl + 'VAS/VAS_024_TotalOutstandingWidget/GetTotalOutstandingKpi',
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
            $container.on('click', '.vas-towdg-open', function (e) {
                e.preventDefault();
                e.stopPropagation();
                openOutstandingDialog();
            });
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
            var trendWord = isWorsening ? msg('VAS_024_Worsening', 'worsening') : msg('VAS_024_Improving', 'improving');
            var arrowPoints = isWorsening ? '6 9 12 15 18 9' : '18 15 12 9 6 15';

            var sparkSvg = buildSparklineSvg(data.SparklineData || []);

            var html = '<div class="vas-towdg-label">' + msg('VAS_024_TotalOutstanding', 'Total Outstanding') + '</div>'
                + '<div class="vas-towdg-value" id="vas_towdg_val_' + widgetID + '">'
                +   sym + outstandingFormatted
                + '</div>'
                + '<div class="vas-towdg-pills-row">'
                +   '<div class="vas-towdg-pill">'
                +     '<span class="vas-towdg-pill-label">' + msg('VAS_024_Invoices', 'Invoices') + '</span>'
                +     '<span class="vas-towdg-pill-value">' + data.InvoiceCount + '</span>'
                +   '</div>'
                +   '<div class="vas-towdg-pill">'
                +     '<span class="vas-towdg-pill-label">' + msg('VAS_024_Vendors', 'Vendors') + '</span>'
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
                + sparkSvg
                + '<button type="button" class="vas-towdg-open" aria-label="'
                +   toEsc(msg('VAS_024_OpenDrilldown', 'Open outstanding payables breakdown'))
                + '"></button>';

            $container.append(html);
        }

        function openOutstandingDialog() {
            if ($outstandingDialog) {
                return;
            }

            $outstandingDialog = $('<div class="vas-towdg-dialog">');
            $('body').append($outstandingDialog);

            $outstandingDialog.dialog({
                autoOpen: false,
                modal: true,
                resizable: false,
                title: msg('VAS_024_OutstandingBreakdown', 'Outstanding Payables - Drill-down'),
                width: Math.min(780, Math.max(320, $(window).width() - 40)),
                minHeight: 300,
                maxHeight: Math.max(320, $(window).height() - 80),
                dialogClass: 'vas-towdg-dialog-shell',
                close: function () {
                    $outstandingDialog.dialog('destroy');
                    $outstandingDialog.remove();
                    $outstandingDialog = null;
                    drillData = null;
                    drillPage = 0;
                }
            });

            // Remove jQuery UI's default close button and inject our own fully-controlled one.
            var $toWidget = $outstandingDialog.dialog('widget');
            $toWidget.find('.ui-dialog-titlebar-close').remove();
            var $toClose = $('<button type="button" class="vas-towdg-dialog-close" aria-label="' + toEsc(msg('VAS_024_Close', 'Close')) + '"><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false"><path fill-rule="evenodd" clip-rule="evenodd" d="M0.331804 0.359434C0.544503 0.146971 0.832843 0.0276306 1.13348 0.0276306C1.43411 0.0276306 1.72245 0.146971 1.93515 0.359434L7.93569 6.36131L13.9376 0.359434C14.0415 0.248321 14.1667 0.159252 14.3058 0.0975303C14.4449 0.0358082 14.5949 0.00269401 14.7471 0.000157642C14.8992 -0.00237873 15.0503 0.0257147 15.1913 0.0827665C15.3324 0.139818 15.4605 0.224663 15.5681 0.332249C15.6757 0.439836 15.7605 0.567966 15.8176 0.709016C15.8746 0.850065 15.9027 1.00115 15.9002 1.15328C15.8976 1.30541 15.8645 1.45547 15.8028 1.59454C15.7411 1.73361 15.652 1.85884 15.5409 1.96278L9.53904 7.96332L15.5409 13.9652C15.742 14.1801 15.8516 14.4648 15.8467 14.759C15.8418 15.0533 15.7227 15.3341 15.5146 15.5422C15.3065 15.7504 15.0257 15.8694 14.7314 15.8743C14.4371 15.8792 14.1525 15.7696 13.9376 15.5685L7.93569 9.56667L1.93515 15.5685C1.72023 15.7696 1.43557 15.8792 1.14131 15.8743C0.847042 15.8694 0.566203 15.7504 0.358097 15.5422C0.149991 15.3341 0.0309118 15.0533 0.0260057 14.759C0.0210996 14.4648 0.130751 14.1801 0.331804 13.9652L6.33234 7.96332L0.331804 1.96278C0.119341 1.75008 0 1.46174 0 1.16111C0 0.860474 0.119341 0.572134 0.331804 0.359434Z"></path></svg></button>');
            $toClose.on('click', function () { $outstandingDialog.dialog('close'); });
            $toWidget.find('.ui-dialog-titlebar').append($toClose);

            loadOutstandingDrilldown();
        }

        function loadOutstandingDrilldown() {
            $outstandingDialog.html('<div class="vas-towdg-drill-state">' + toEsc(msg('VAS_024_Loading', 'Loading...')) + '</div>');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_024_TotalOutstandingWidget/GetOutstandingDrilldown',
                dataType: 'json',
                async: true,
                success: function (data) {
                    var drilldown = typeof data === 'string' ? JSON.parse(data) : data;
                    renderOutstandingDrilldown(drilldown);
                },
                error: function () {
                    if (!$outstandingDialog) { return; }
                    $outstandingDialog.html('<div class="vas-towdg-drill-state vas-towdg-drill-error">'
                        + toEsc(msg('VAS_024_DrilldownError', 'Unable to load outstanding payables details.'))
                        + '</div>');
                    showOutstandingDialog();
                }
            });
        }

        function renderOutstandingDrilldown(data) {
            if (!$outstandingDialog) { return; }

            var vendors = (data && data.Vendors) || [];
            if (!vendors.length) {
                $outstandingDialog.html('<div class="vas-towdg-drill-state">'
                    + toEsc(msg('VAS_024_NoOutstandingData', 'No outstanding purchase payables found.'))
                    + '</div>');
                showOutstandingDialog();
                return;
            }

            drillData = data;
            drillPage = 0;
            paintOutstandingDrilldown();
        }

        /* Renders the current drill-down page. Rule 18: client-side paging over
           all vendors; Rule 14/15: each amount carries a hover tooltip with the
           currency symbol and the full thousand-separated value. */
        function paintOutstandingDrilldown() {
            if (!$outstandingDialog || !drillData) { return; }

            var data = drillData;
            var vendors = data.Vendors || [];
            var sym = data.CurSymbol || '';
            var precision = data.StdPrecision != null ? data.StdPrecision : 2;
            var total = data.OutstandingTotal ? data.OutstandingTotal : 0;
            var trendPct = 0;
            if (data.LastMonthOutstanding && data.LastMonthOutstanding !== 0) {
                trendPct = ((data.CurrentMonthOutstanding - data.LastMonthOutstanding) / Math.abs(data.LastMonthOutstanding)) * 100;
            } else if (data.CurrentMonthOutstanding > 0) {
                trendPct = 100;
            }

            var isWorsening = trendPct >= 0;
            var trendWord = isWorsening ? msg('VAS_024_Worsening', 'worsening') : msg('VAS_024_Improving', 'improving');
            var trendClass = isWorsening ? 'vas-towdg-drill-trend-bad' : 'vas-towdg-drill-trend-good';
            var summaryText = formatQty(data.InvoiceCount || 0) + ' ' + msg('VAS_024_InvoicesLower', 'invoices')
                + ' ' + msg('VAS_024_RemainUnpaid', 'remain unpaid across') + ' '
                + formatQty(data.VendorCount || 0) + ' ' + msg('VAS_024_VendorsLower', 'vendors') + '. ';
            if ((data.OverdueInvoiceCount || 0) > 0) {
                summaryText += msg('VAS_024_OverdueAttention', 'Immediate attention required for overdue invoices.');
            }

            var maxAmount = 0;
            for (var i = 0; i < vendors.length; i++) {
                maxAmount = Math.max(maxAmount, Number(vendors[i].OutstandingAmount || 0));
            }

            var pageCount = Math.ceil(vendors.length / DRILL_PAGE_SIZE);
            if (drillPage >= pageCount) { drillPage = pageCount - 1; }
            if (drillPage < 0) { drillPage = 0; }
            var start = drillPage * DRILL_PAGE_SIZE;
            var end = Math.min(start + DRILL_PAGE_SIZE, vendors.length);

            var totalTip = sym + ' ' + formatFull(total, precision);
            var html = '<div class="vas-towdg-drill">'
                + '<div class="vas-towdg-drill-card">'
                +   '<span class="vas-towdg-drill-card-label">' + toEsc(msg('VAS_024_TotalOutstanding', 'Total Outstanding')) + '</span>'
                +   '<strong class="vas-towdg-drill-card-value" title="' + toEsc(totalTip) + '">' + toEsc(sym + formatAmount(total, precision)) + '</strong>'
                +   '<em class="vas-towdg-drill-card-trend ' + trendClass + '">' + (trendPct >= 0 ? '+' : '')
                +     Math.abs(trendPct).toFixed(1) + '% ' + toEsc(trendWord) + '</em>'
                + '</div>'
                + '<div class="vas-towdg-drill-copy">'
                +   '<p class="vas-towdg-drill-desc">' + toEsc(summaryText) + '</p>'
                +   '<div class="vas-towdg-drill-rows">';

            for (var v = start; v < end; v++) {
                var vendor = vendors[v];
                var amount = Number(vendor.OutstandingAmount || 0);
                var width = maxAmount > 0 ? (amount / maxAmount) * 100 : 0;
                width = Math.max(0, Math.min(100, width));
                var barWidth = width > 0 ? Math.max(2, width) : 0;
                var rowTip = sym + ' ' + formatFull(amount, precision);

                html += '<div class="vas-towdg-drill-row">'
                    + '<div class="vas-towdg-drill-row-head">'
                    +   '<span class="vas-towdg-drill-row-name">' + toEsc(vendor.VendorName || '-') + '</span>'
                    +   '<span class="vas-towdg-drill-row-val" title="' + toEsc(rowTip) + '">' + toEsc(sym + formatAmount(amount, precision)) + '</span>'
                    + '</div>'
                    + '<div class="vas-towdg-drill-track">'
                    +   '<div class="vas-towdg-drill-fill" style="width:' + barWidth.toFixed(1) + '%"></div>'
                    + '</div>'
                    + '</div>';
            }

            // Keep every popup the same (max) height: always pad the page up to
            // DRILL_PAGE_SIZE with invisible placeholder rows, so a short page or a
            // single-page popup is the same size as a full 6-row page.
            if (pageCount >= 1) {
                for (var pad = end - start; pad < DRILL_PAGE_SIZE; pad++) {
                    html += '<div class="vas-towdg-drill-row vas-towdg-drill-row--ph" aria-hidden="true">'
                        + '<div class="vas-towdg-drill-row-head"><span class="vas-towdg-drill-row-name">&nbsp;</span><span class="vas-towdg-drill-row-val">&nbsp;</span></div>'
                        + '<div class="vas-towdg-drill-track"></div>'
                        + '</div>';
                }
            }

            html += '</div>';
            html += buildDrillPager(drillPage, pageCount, vendors.length);
            html += '</div></div>';
            $outstandingDialog.html(html);

            $outstandingDialog.find('.vas-towdg-drill-pager-prev').on('click', function () {
                if (drillPage > 0) { drillPage--; paintOutstandingDrilldown(); }
            });
            $outstandingDialog.find('.vas-towdg-drill-pager-next').on('click', function () {
                if (drillPage < pageCount - 1) { drillPage++; paintOutstandingDrilldown(); }
            });

            showOutstandingDialog();
        }

        /* Footer pager (rule 18): "Showing X-Y of Z" on the left, "<  n of m  >" on
           the right (24px buttons / 14px chevrons per the design spec). */
        function buildDrillPager(page, pageCount, total) {
            // Always render the pager so the popup keeps a uniform height; on a single
            // page both nav buttons simply stay disabled ("1 of 1").
            if (pageCount < 1) { return ''; }
            var start = page * DRILL_PAGE_SIZE + 1;
            var end = Math.min((page + 1) * DRILL_PAGE_SIZE, total);
            var prevDis = page <= 0 ? ' disabled' : '';
            var nextDis = page >= pageCount - 1 ? ' disabled' : '';
            var showing = msg('VAS_024_Showing', 'Showing') + ' ' + start + '–' + end
                + ' ' + msg('VAS_024_Of', 'of') + ' ' + total;
            var ofLabel = (page + 1) + ' ' + msg('VAS_024_Of', 'of') + ' ' + pageCount;
            return '<div class="vas-towdg-drill-pager">'
                + '<span class="vas-towdg-drill-pager-info">' + toEsc(showing) + '</span>'
                + '<div class="vas-towdg-drill-pager-nav">'
                +   '<button type="button" class="vas-towdg-drill-pager-btn vas-towdg-drill-pager-prev"' + prevDis
                +     ' aria-label="' + toEsc(msg('VAS_024_PrevPage', 'Previous page')) + '">'
                +     '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>'
                +   '</button>'
                +   '<span class="vas-towdg-drill-pager-label">' + toEsc(ofLabel) + '</span>'
                +   '<button type="button" class="vas-towdg-drill-pager-btn vas-towdg-drill-pager-next"' + nextDis
                +     ' aria-label="' + toEsc(msg('VAS_024_NextPage', 'Next page')) + '">'
                +     '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>'
                +   '</button>'
                + '</div>'
                + '</div>';
        }

        /* Full, thousand-separated amount for hover tooltips (rules 14/15).
           Precision comes from the system standard precision, never hard-coded. */
        function formatFull(number, stdPrecision) {
            var prec = VIS.Env.getCtx().getStdPrecision();
            if (prec == null || isNaN(prec)) { prec = stdPrecision != null ? stdPrecision : 2; }
            return Number(number || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: prec,
                maximumFractionDigits: prec
            });
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
                unit = msg('VAS_024_Trillion', 'T');
                formatted = (absNumber / 1000000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000000000) {
                unit = msg('VAS_024_Billion', 'B');
                formatted = (absNumber / 1000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 10000000) {
                unit = msg('VAS_024_Crore', 'Cr');
                formatted = (absNumber / 10000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 100000) {
                unit = msg('VAS_024_Lakh', 'L');
                formatted = (absNumber / 100000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000) {
                unit = msg('VAS_024_Thousand', 'K');
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

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, {
                maximumFractionDigits: 2
            });
        }

        function msg(key, fallback) {
            var value = VIS.Msg.getMsg(key);
            return value && value !== key && value !== '[' + key + ']' ? value : fallback;
        }

        function showOutstandingDialog() {
            if (!$outstandingDialog) { return; }
            $outstandingDialog.dialog('open');
            $outstandingDialog.dialog('option', 'position', { my: 'center', at: 'center', of: window });
        }

        function toEsc(value) {
            return $('<div>').text(value == null ? '' : value).html();
        }

        this.closeOutstandingDialog = function () {
            if ($outstandingDialog) {
                if ($outstandingDialog.dialog('isOpen')) {
                    $outstandingDialog.dialog('close');
                } else {
                    $outstandingDialog.dialog('destroy');
                    $outstandingDialog.remove();
                    $outstandingDialog = null;
                    drillData = null;
                    drillPage = 0;
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
    VAS.VAS_024_TotalOutstandingWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_024_TotalOutstandingWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_024_TotalOutstandingWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_024_TotalOutstandingWidget.prototype.dispose = function () {
        if (this.closeOutstandingDialog) {
            this.closeOutstandingDialog();
        }
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
