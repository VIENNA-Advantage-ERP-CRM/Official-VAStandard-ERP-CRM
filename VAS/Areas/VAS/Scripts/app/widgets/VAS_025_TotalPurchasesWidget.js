/************************************************************
 * Module Name    : VAS
 * Purpose        : Total Purchases (MTD) KPI Widget
 *                  Shows MTD total, YTD total, invoice count,
 *                  trend vs last month, and a 7-month sparkline.
 * chronological  : Development
 * Created Date   : 12 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys used in this file (add via System Messages):
 *   VAS_025_TotalPurchasesMTD  => "Total Purchases (MTD)"
 *   VAS_025_YTD                => "YTD"
 *   VAS_025_INV                => "INV"
 *   VAS_025_VsLastMonth        => "vs last month"
 *   VAS_025_Crore              => "Cr"
 *   VAS_025_Lakh               => "L"
 *   VAS_025_Thousand           => "K"
 *   VAS_025_Million            => "M"
 *   VAS_025_Billion            => "B"
 *   VAS_025_Trillion           => "T"
 *   VAS_025_OpenDrilldown      => "Open material category breakdown"
 *   VAS_025_MaterialCategoryBreakdown => "Material Category Breakdown"
 *   VAS_025_MaterialSpendMTD   => "Material Spend (MTD)"
 *   VAS_025_TotalPurchasesMTD  => "Total Purchases (MTD)"
 *   VAS_025_ProcessedIntro     => "This month you have processed"
 *   VAS_025_PurchaseInvoicesLower => "purchase invoices"
 *   VAS_025_AcrossLower        => "across"
 *   VAS_025_CategoriesLower    => "categories"
 *   VAS_025_TopSpenderIs       => "The top spender is"
 *   VAS_025_AtLower            => "at"
 *   VAS_025_OtherChargesTax    => "Other (charges & tax)"
 *   VAS_025_Categories         => "Categories"
 *   VAS_025_Lines              => "Lines"
 *   VAS_025_Category           => "Category"
 *   VAS_025_Amount             => "Amount"
 *   VAS_025_Share              => "Share"
 *   VAS_025_TopSpender         => "Top spender"
 *   VAS_025_Qty                => "Qty"
 *   VAS_025_Loading            => "Loading..."
 *   VAS_025_NoCategoryData     => "No material category data found for this month."
 *   VAS_025_DrilldownError     => "Unable to load material category details."
 *   VAS_025_Close              => "Close"
 *   VAS_025_Showing            => "Showing"
 *   VAS_025_Of                 => "of"
 *   VAS_025_PrevPage           => "Previous page"
 *   VAS_025_NextPage           => "Next page"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_025_TotalPurchasesWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-tpwidg-root">');
        var $container;
        var $categoryDialog = null;
        var widgetID = null;
        var drillData = null;          // cached drill-down payload for client-side paging
        var drillPage = 0;             // current 0-based page in the drill-down popup
        var DRILL_PAGE_SIZE = 6;       // category rows shown per page (rule 18 - paging)

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
                url: VIS.Application.contextUrl + 'VAS/VAS_025_TotalPurchasesWidget/GetTotalPurchasesKpi',
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
            $container = $('<div class="vas-tpwidg-container" id="vas_tpwidg_cont_' + widgetID + '">');
            $container.on('click', '.vas-tpwidg-open', function (e) {
                e.preventDefault();
                e.stopPropagation();
                openCategoryDialog();
            });
            $root.append($container);
        }

        /* ---- Render KPI content ---- */
        function renderKpi(data) {
            $container.empty();

            var sym = data.CurSymbol || '';
            var mtdFormatted = formatAmount(data.MtdTotal, data.StdPrecision);
            var ytdFormatted = formatAmount(data.YtdTotal, data.StdPrecision);

            var trendPct = 0;
            if (data.LastMonthTotal && data.LastMonthTotal !== 0) {
                trendPct = ((data.MtdTotal - data.LastMonthTotal) / Math.abs(data.LastMonthTotal)) * 100;
            } else if (data.MtdTotal > 0) {
                trendPct = 100;
            }
            var trendUp = trendPct >= 0;
            var trendClass = trendUp ? 'vas-tpwidg-trend-up' : 'vas-tpwidg-trend-down';
            var trendSign = trendUp ? '+' : '';
            // Up arrow: 18,15 → 12,9 → 6,15  |  Down arrow: 6,9 → 12,15 → 18,9
            var arrowPoints = trendUp ? '18 15 12 9 6 15' : '6 9 12 15 18 9';

            var sparkSvg = buildSparklineSvg(data.SparklineData || []);

            var html = '<div class="vas-tpwidg-label">' + msg('VAS_025_TotalPurchasesMTD', 'Total Purchases (MTD)') + '</div>'
                + '<div class="vas-tpwidg-value" id="vas_tpwidg_val_' + widgetID + '">'
                +   sym + mtdFormatted
                + '</div>'
                + '<div class="vas-tpwidg-pills-row">'
                +   '<div class="vas-tpwidg-pill">'
                +     '<span class="vas-tpwidg-pill-label">' + msg('VAS_025_YTD', 'YTD') + '</span>'
                +     '<span class="vas-tpwidg-pill-value">' + sym + ytdFormatted + '</span>'
                +   '</div>'
                +   '<div class="vas-tpwidg-pill">'
                +     '<span class="vas-tpwidg-pill-label">' + msg('VAS_025_INV', 'INV') + '</span>'
                +     '<span class="vas-tpwidg-pill-value">' + data.InvoiceCount + '</span>'
                +   '</div>'
                + '</div>'
                + '<div class="vas-tpwidg-trend-row ' + trendClass + '">'
                +   '<svg class="vas-tpwidg-trend-icon" viewBox="0 0 24 24" fill="none"'
                +       ' stroke="currentColor" stroke-width="2.5"'
                +       ' stroke-linecap="round" stroke-linejoin="round">'
                +     '<polyline points="' + arrowPoints + '"/>'
                +   '</svg>'
                +   trendSign + Math.abs(trendPct).toFixed(1) + '% ' + msg('VAS_025_VsLastMonth', 'vs last month')
                + '</div>'
                + sparkSvg
                + '<button type="button" class="vas-tpwidg-open" aria-label="'
                +   tpEsc(msg('VAS_025_OpenDrilldown', 'Open material category breakdown'))
                + '"></button>';

            $container.append(html);
        }

        function openCategoryDialog() {
            if ($categoryDialog) {
                return;
            }

            $categoryDialog = $('<div class="vas-tpwidg-dialog">');
            $('body').append($categoryDialog);

            $categoryDialog.dialog({
                autoOpen: false,
                modal: true,
                resizable: false,
                title: msg('VAS_025_MaterialCategoryBreakdown', 'Material Category Breakdown'),
                width: Math.min(780, Math.max(320, $(window).width() - 40)),
                minHeight: 300,
                maxHeight: Math.max(320, $(window).height() - 80),
                dialogClass: 'vas-tpwidg-dialog-shell',
                close: function () {
                    $categoryDialog.dialog('destroy');
                    $categoryDialog.remove();
                    $categoryDialog = null;
                    drillData = null;
                    drillPage = 0;
                }
            });

            // Remove jQuery UI's default close button and inject our own fully-controlled one.
            var $tpWidget = $categoryDialog.dialog('widget');
            $tpWidget.find('.ui-dialog-titlebar-close').remove();
            var $tpClose = $('<button type="button" class="vas-tpwidg-dialog-close" aria-label="' + tpEsc(msg('VAS_025_Close', 'Close')) + '"><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false"><path fill-rule="evenodd" clip-rule="evenodd" d="M0.331804 0.359434C0.544503 0.146971 0.832843 0.0276306 1.13348 0.0276306C1.43411 0.0276306 1.72245 0.146971 1.93515 0.359434L7.93569 6.36131L13.9376 0.359434C14.0415 0.248321 14.1667 0.159252 14.3058 0.0975303C14.4449 0.0358082 14.5949 0.00269401 14.7471 0.000157642C14.8992 -0.00237873 15.0503 0.0257147 15.1913 0.0827665C15.3324 0.139818 15.4605 0.224663 15.5681 0.332249C15.6757 0.439836 15.7605 0.567966 15.8176 0.709016C15.8746 0.850065 15.9027 1.00115 15.9002 1.15328C15.8976 1.30541 15.8645 1.45547 15.8028 1.59454C15.7411 1.73361 15.652 1.85884 15.5409 1.96278L9.53904 7.96332L15.5409 13.9652C15.742 14.1801 15.8516 14.4648 15.8467 14.759C15.8418 15.0533 15.7227 15.3341 15.5146 15.5422C15.3065 15.7504 15.0257 15.8694 14.7314 15.8743C14.4371 15.8792 14.1525 15.7696 13.9376 15.5685L7.93569 9.56667L1.93515 15.5685C1.72023 15.7696 1.43557 15.8792 1.14131 15.8743C0.847042 15.8694 0.566203 15.7504 0.358097 15.5422C0.149991 15.3341 0.0309118 15.0533 0.0260057 14.759C0.0210996 14.4648 0.130751 14.1801 0.331804 13.9652L6.33234 7.96332L0.331804 1.96278C0.119341 1.75008 0 1.46174 0 1.16111C0 0.860474 0.119341 0.572134 0.331804 0.359434Z"></path></svg></button>');
            $tpClose.on('click', function () { $categoryDialog.dialog('close'); });
            $tpWidget.find('.ui-dialog-titlebar').append($tpClose);

            loadCategoryDrilldown();
        }

        function loadCategoryDrilldown() {
            $categoryDialog.html('<div class="vas-tpwidg-drill-state">' + tpEsc(msg('VAS_025_Loading', 'Loading...')) + '</div>');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_025_TotalPurchasesWidget/GetPurchaseCategoryDrilldown',
                dataType: 'json',
                async: true,
                success: function (data) {
                    var drilldown = typeof data === 'string' ? JSON.parse(data) : data;
                    renderCategoryDrilldown(drilldown);
                },
                error: function () {
                    if (!$categoryDialog) { return; }
                    $categoryDialog.html('<div class="vas-tpwidg-drill-state vas-tpwidg-drill-error">'
                        + tpEsc(msg('VAS_025_DrilldownError', 'Unable to load material category details.'))
                        + '</div>');
                    showCategoryDialog();
                }
            });
        }

        function renderCategoryDrilldown(data) {
            if (!$categoryDialog) { return; }

            var categories = (data && data.Categories) || [];
            if (!categories.length) {
                $categoryDialog.html('<div class="vas-tpwidg-drill-state">'
                    + tpEsc(msg('VAS_025_NoCategoryData', 'No material category data found for this month.'))
                    + '</div>');
                showCategoryDialog();
                return;
            }

            drillData = data;
            drillPage = 0;
            paintCategoryDrilldown();
        }

        /* Renders the current drill-down page. Rule 18: client-side paging over
           all categories; Rule 14/15: each amount carries a hover tooltip with the
           currency symbol and the full thousand-separated value. */
        function paintCategoryDrilldown() {
            if (!$categoryDialog || !drillData) { return; }

            var data = drillData;
            var categories = data.Categories || [];
            var sym = data.CurSymbol || '';
            var precision = data.StdPrecision != null ? data.StdPrecision : 2;
            var total = data.TotalAmount ? data.TotalAmount : 0;
            var catCount = data.CategoryCount || categories.length;
            var lineCount = data.LineCount || 0;

            var maxAmount = 0;
            for (var i = 0; i < categories.length; i++) {
                maxAmount = Math.max(maxAmount, Number(categories[i].Amount || 0));
            }

            var subtitle = formatQty(catCount) + ' ' + msg('VAS_025_Categories', 'Categories')
                + ' · ' + formatQty(lineCount) + ' ' + msg('VAS_025_Lines', 'Lines');

            var topName = data.TopName || (categories.length ? categories[0].Name : '');
            var topAmount = data.TopAmount != null ? Number(data.TopAmount) : 0;
            var summaryText = msg('VAS_025_ProcessedIntro', 'This month you have processed') + ' '
                + formatQty(data.InvoiceCount || 0) + ' ' + msg('VAS_025_PurchaseInvoicesLower', 'purchase invoices')
                + ' ' + msg('VAS_025_AcrossLower', 'across') + ' '
                + formatQty(catCount) + ' ' + msg('VAS_025_CategoriesLower', 'categories') + '.';
            if (topName) {
                summaryText += ' ' + msg('VAS_025_TopSpenderIs', 'The top spender is') + ' ' + topName
                    + ' ' + msg('VAS_025_AtLower', 'at') + ' ' + sym + formatAmount(topAmount, precision) + '.';
            }

            var pageCount = Math.ceil(categories.length / DRILL_PAGE_SIZE);
            if (drillPage >= pageCount) { drillPage = pageCount - 1; }
            if (drillPage < 0) { drillPage = 0; }
            var start = drillPage * DRILL_PAGE_SIZE;
            var end = Math.min(start + DRILL_PAGE_SIZE, categories.length);

            var totalTip = sym + ' ' + formatFull(total, precision);
            var html = '<div class="vas-tpwidg-drill">'
                + '<div class="vas-tpwidg-drill-card">'
                +   '<span class="vas-tpwidg-drill-card-label">' + tpEsc(msg('VAS_025_TotalPurchasesMTD', 'Total Purchases (MTD)')) + '</span>'
                +   '<strong class="vas-tpwidg-drill-card-value" title="' + tpEsc(totalTip) + '">' + tpEsc(sym + formatAmount(total, precision)) + '</strong>'
                +   '<em class="vas-tpwidg-drill-card-trend">' + tpEsc(subtitle) + '</em>'
                + '</div>'
                + '<div class="vas-tpwidg-drill-copy">'
                +   '<p class="vas-tpwidg-drill-desc">' + tpEsc(summaryText) + '</p>'
                +   '<div class="vas-tpwidg-drill-rows">';

            for (var c = start; c < end; c++) {
                var category = categories[c];
                var amount = Number(category.Amount || 0);
                var width = maxAmount > 0 ? (amount / maxAmount) * 100 : 0;
                width = Math.max(0, Math.min(100, width));
                var barWidth = width > 0 ? Math.max(2, width) : 0;
                var rowTip = sym + ' ' + formatFull(amount, precision);

                html += '<div class="vas-tpwidg-drill-row">'
                    + '<div class="vas-tpwidg-drill-row-head">'
                    +   '<span class="vas-tpwidg-drill-row-name">' + tpEsc(category.IsOther ? msg('VAS_025_OtherChargesTax', 'Other (charges & tax)') : (category.Name || '-')) + '</span>'
                    +   '<span class="vas-tpwidg-drill-row-val" title="' + tpEsc(rowTip) + '">' + tpEsc(sym + formatAmount(amount, precision)) + '</span>'
                    + '</div>'
                    + '<div class="vas-tpwidg-drill-track">'
                    +   '<div class="vas-tpwidg-drill-fill" style="width:' + barWidth.toFixed(1) + '%"></div>'
                    + '</div>'
                    + '</div>';
            }

            // Keep every popup the same (max) height: always pad the page up to
            // DRILL_PAGE_SIZE with invisible placeholder rows, so a short page or a
            // single-page popup is the same size as a full 6-row page.
            if (pageCount >= 1) {
                for (var pad = end - start; pad < DRILL_PAGE_SIZE; pad++) {
                    html += '<div class="vas-tpwidg-drill-row vas-tpwidg-drill-row--ph" aria-hidden="true">'
                        + '<div class="vas-tpwidg-drill-row-head"><span class="vas-tpwidg-drill-row-name">&nbsp;</span><span class="vas-tpwidg-drill-row-val">&nbsp;</span></div>'
                        + '<div class="vas-tpwidg-drill-track"></div>'
                        + '</div>';
                }
            }

            html += '</div>';
            html += buildDrillPager(drillPage, pageCount, categories.length);
            html += '</div></div>';
            $categoryDialog.html(html);

            $categoryDialog.find('.vas-tpwidg-drill-pager-prev').on('click', function () {
                if (drillPage > 0) { drillPage--; paintCategoryDrilldown(); }
            });
            $categoryDialog.find('.vas-tpwidg-drill-pager-next').on('click', function () {
                if (drillPage < pageCount - 1) { drillPage++; paintCategoryDrilldown(); }
            });

            showCategoryDialog();
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
            var showing = msg('VAS_025_Showing', 'Showing') + ' ' + start + '–' + end
                + ' ' + msg('VAS_025_Of', 'of') + ' ' + total;
            var ofLabel = (page + 1) + ' ' + msg('VAS_025_Of', 'of') + ' ' + pageCount;
            return '<div class="vas-tpwidg-drill-pager">'
                + '<span class="vas-tpwidg-drill-pager-info">' + tpEsc(showing) + '</span>'
                + '<div class="vas-tpwidg-drill-pager-nav">'
                +   '<button type="button" class="vas-tpwidg-drill-pager-btn vas-tpwidg-drill-pager-prev"' + prevDis
                +     ' aria-label="' + tpEsc(msg('VAS_025_PrevPage', 'Previous page')) + '">'
                +     '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>'
                +   '</button>'
                +   '<span class="vas-tpwidg-drill-pager-label">' + tpEsc(ofLabel) + '</span>'
                +   '<button type="button" class="vas-tpwidg-drill-pager-btn vas-tpwidg-drill-pager-next"' + nextDis
                +     ' aria-label="' + tpEsc(msg('VAS_025_NextPage', 'Next page')) + '">'
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
                unit = msg('VAS_025_Trillion', 'T');
                formatted = (absNumber / 1000000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000000000) {
                unit = msg('VAS_025_Billion', 'B');
                formatted = (absNumber / 1000000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 10000000) {
                unit = msg('VAS_025_Crore', 'Cr');
                formatted = (absNumber / 10000000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 100000) {
                unit = msg('VAS_025_Lakh', 'L');
                formatted = (absNumber / 100000).toLocaleString(window.navigator.language, opts2);
            } else if (absNumber >= 1000) {
                unit = msg('VAS_025_Thousand', 'K');
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
            return '<svg class="vas-tpwidg-sparkline" width="' + W + '" height="' + H
                + '" viewBox="0 0 ' + W + ' ' + H + '">'
                + '<polyline points="' + pts.join(' ') + '" fill="none"'
                + ' stroke="#0083DA" stroke-width="2.2" stroke-linecap="round"/>'
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

        function showCategoryDialog() {
            if (!$categoryDialog) { return; }
            $categoryDialog.dialog('open');
            $categoryDialog.dialog('option', 'position', { my: 'center', at: 'center', of: window });
        }

        function tpEsc(value) {
            return $('<div>').text(value == null ? '' : value).html();
        }

        this.closeCategoryDialog = function () {
            if ($categoryDialog) {
                if ($categoryDialog.dialog('isOpen')) {
                    $categoryDialog.dialog('close');
                } else {
                    $categoryDialog.dialog('destroy');
                    $categoryDialog.remove();
                    $categoryDialog = null;
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
    VAS.VAS_025_TotalPurchasesWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_025_TotalPurchasesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_025_TotalPurchasesWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_025_TotalPurchasesWidget.prototype.dispose = function () {
        if (this.closeCategoryDialog) {
            this.closeCategoryDialog();
        }
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
