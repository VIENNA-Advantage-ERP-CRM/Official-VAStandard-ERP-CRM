/************************************************************
 * Module Name    : VAS
 * Purpose        : Total Purchases (MTD) KPI Widget
 *                  Shows MTD total and trend vs last month, with a
 *                  server-paged material-category breakdown drill-down.
 * chronological  : Development
 * Created Date   : 12 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys used in this file (add via System Messages):
 *   VAS_025_TotalPurchasesMTD  => "Total Purchases"
 *   VAS_025_MonthToDate        => "Month to date"
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
 *   VAS_025_MaterialSpendMTD   => "Material Spend"
 *   VAS_025_TotalPurchasesMTD  => "Total Purchases"
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

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the widget's em-anchor clamps resolve against the dashboard's
       visible width, not the viewport. One document-level ResizeObserver serves every
       widget; without a marked container — or without ResizeObserver — the CSS falls
       back to 100vw. (Mirrors VAS_018_OverduePayablesWidget.) */
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

    VAS.VAS_025_TotalPurchasesWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-tpwidg-root">');
        var $container;
        var $categoryDialog = null;
        var widgetID = null;
        var drillPage = 1;             // current 1-based page in the drill-down popup (server-side)
        var DRILL_PAGE_SIZE = 6;       // category rows requested per page (rule 18 - paging)

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
            // The clickable overlay is a <div role="button">, so it has no native
            // keyboard activation — wire Enter / Space up manually (mirrors VAS_018).
            $container.on('keydown', '.vas-tpwidg-open', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault();
                    e.stopPropagation();
                    openCategoryDialog();
                }
            });
            $root.append($container);
        }

        /* ---- Render KPI content ---- */
        function renderKpi(data) {
            $container.empty();

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

            // Header icon: invoice/document carrying a $, with a $-in-circle badge —
            // the Total Purchases glyph (design per attached image). Stroke inherits the
            // icon well's blue via currentColor. Structure mirrors VAS_018's header.
            var iconSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">'
                +   '<path d="M13.5 3H6a1 1 0 0 0-1 1v15a1 1 0 0 0 1 1h4.2"/>'
                +   '<path d="M13.5 3l4.5 4.5V11"/>'
                +   '<path d="M13.5 3v4.5H18"/>'
                +   '<path d="M9 6.2c-1 0-1.7.5-1.7 1.2 0 1.6 3.2.7 3.2 2.3 0 .7-.7 1.2-1.7 1.2s-1.7-.5-1.7-1.1"/>'
                +   '<path d="M9 5.2v1M9 11v1"/>'
                +   '<path d="M8 14.5h3.5M8 17h2"/>'
                +   '<circle cx="17" cy="17" r="4.6"/>'
                +   '<path d="M18.1 15.4c0-.5-.5-.9-1.1-.9-.7 0-1.2.4-1.2.9 0 1.1 2.2.5 2.2 1.6 0 .5-.5.9-1.1.9s-1.1-.4-1.1-.8"/>'
                +   '<path d="M17 13.8v.6M17 19v.6"/>'
                + '</svg>';

            var html = '<div class="vas-tpwidg-header">'
                +   '<div class="vas-tpwidg-icon">' + iconSvg + '</div>'
                +   '<div class="vas-tpwidg-head-text">'
                +     '<div class="vas-tpwidg-label">' + msg('VAS_025_TotalPurchasesMTD', 'Total Purchases') + '</div>'
                +     '<div class="vas-tpwidg-subtitle">' + msg('VAS_025_MonthToDate', 'Month to date') + '</div>'
                +   '</div>'
                + '</div>'
                + '<div class="vas-tpwidg-value" id="vas_tpwidg_val_' + widgetID + '">'
                +   formatMetric(data.MtdTotal, data.CurSymbol, data.CurIso, data.StdPrecision)
                + '</div>'
                + '<div class="vas-tpwidg-trend-row ' + trendClass + '">'
                +   '<svg class="vas-tpwidg-trend-icon" viewBox="0 0 24 24" fill="none"'
                +       ' stroke="currentColor" stroke-width="2.5"'
                +       ' stroke-linecap="round" stroke-linejoin="round">'
                +     '<polyline points="' + arrowPoints + '"/>'
                +   '</svg>'
                +   trendSign + Math.abs(trendPct).toFixed(1) + '% ' + msg('VAS_025_VsLastMonth', 'vs last month')
                + '</div>'
                + '<div class="vas-tpwidg-open" role="button" tabindex="0" aria-label="'
                +   tpEsc(msg('VAS_025_OpenDrilldown', 'Open material category breakdown'))
                + '"></div>';

            $container.append(html);
        }

        /* Compose the KPI metric via the shared CurrencyFormat util
           (VIS.Util.formatCompactAmount): sign, then the currency symbol, then the
           compact magnitude (Indian vs international per the base-currency ISO).
           Mirrors VAS_018_OverduePayablesWidget.formatMetric. */
        function formatMetric(value, symbol, isoCode, precision) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var absStr = VIS.Util.formatCompactAmount(value, isoCode, precision);
            return sign + (symbol || '') + absStr;
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
                // Fixed height (clamped to the viewport) so the busy spinner, a short
                // page, and a full page all render at the same size — the popup never
                // resizes as you page. The body fills it via flex (see CSS).
                height: Math.min(350, Math.max(300, $(window).height() - 80)),
                dialogClass: 'vas-tpwidg-dialog-shell',
                close: function () {
                    $categoryDialog.dialog('destroy');
                    $categoryDialog.remove();
                    $categoryDialog = null;
                    drillPage = 1;
                }
            });

            // Remove jQuery UI's default close button and inject our own fully-controlled one.
            var $tpWidget = $categoryDialog.dialog('widget');
            $tpWidget.find('.ui-dialog-titlebar-close').remove();
            var $tpClose = $('<button type="button" class="vas-tpwidg-dialog-close" aria-label="' + tpEsc(msg('VAS_025_Close', 'Close')) + '"><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false"><path fill-rule="evenodd" clip-rule="evenodd" d="M0.331804 0.359434C0.544503 0.146971 0.832843 0.0276306 1.13348 0.0276306C1.43411 0.0276306 1.72245 0.146971 1.93515 0.359434L7.93569 6.36131L13.9376 0.359434C14.0415 0.248321 14.1667 0.159252 14.3058 0.0975303C14.4449 0.0358082 14.5949 0.00269401 14.7471 0.000157642C14.8992 -0.00237873 15.0503 0.0257147 15.1913 0.0827665C15.3324 0.139818 15.4605 0.224663 15.5681 0.332249C15.6757 0.439836 15.7605 0.567966 15.8176 0.709016C15.8746 0.850065 15.9027 1.00115 15.9002 1.15328C15.8976 1.30541 15.8645 1.45547 15.8028 1.59454C15.7411 1.73361 15.652 1.85884 15.5409 1.96278L9.53904 7.96332L15.5409 13.9652C15.742 14.1801 15.8516 14.4648 15.8467 14.759C15.8418 15.0533 15.7227 15.3341 15.5146 15.5422C15.3065 15.7504 15.0257 15.8694 14.7314 15.8743C14.4371 15.8792 14.1525 15.7696 13.9376 15.5685L7.93569 9.56667L1.93515 15.5685C1.72023 15.7696 1.43557 15.8792 1.14131 15.8743C0.847042 15.8694 0.566203 15.7504 0.358097 15.5422C0.149991 15.3341 0.0309118 15.0533 0.0260057 14.759C0.0210996 14.4648 0.130751 14.1801 0.331804 13.9652L6.33234 7.96332L0.331804 1.96278C0.119341 1.75008 0 1.46174 0 1.16111C0 0.860474 0.119341 0.572134 0.331804 0.359434Z"></path></svg></button>');
            $tpClose.on('click', function () { $categoryDialog.dialog('close'); });
            $tpWidget.find('.ui-dialog-titlebar').append($tpClose);

            loadCategoryDrilldown(1);
        }

        /* Fetch one page of the category breakdown from the server. Opens the dialog
           immediately with the core busy spinner (mirrors VAS_018) so the user sees a
           loading state while each page is fetched — server-side paging. */
        function loadCategoryDrilldown(pageNo) {
            if (!$categoryDialog) { return; }
            if (pageNo < 1) { pageNo = 1; }
            drillPage = pageNo;

            $categoryDialog.html('<div class="vas-tpwidg-drill-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            showCategoryDialog();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_025_TotalPurchasesWidget/GetPurchaseCategoryDrilldown',
                data: { pageNo: pageNo, pageSize: DRILL_PAGE_SIZE },
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

            var total = data ? Number(data.TotalCount || 0) : 0;
            if (!total) {
                $categoryDialog.html('<div class="vas-tpwidg-drill-state">'
                    + tpEsc(msg('VAS_025_NoCategoryData', 'No material category data found for this month.'))
                    + '</div>');
                showCategoryDialog();
                return;
            }

            paintCategoryDrilldown(data);
        }

        /* Renders the current (server-supplied) drill-down page. Rule 18: server-side
           paging over all categories; Rule 14/15: each amount carries a hover tooltip
           with the currency symbol and the full thousand-separated value. */
        function paintCategoryDrilldown(data) {
            if (!$categoryDialog || !data) { return; }

            var categories = data.Categories || [];
            var sym = data.CurSymbol || '';
            var iso = data.CurIso || '';
            var precision = data.StdPrecision != null ? data.StdPrecision : 2;
            var total = data.TotalAmount ? data.TotalAmount : 0;
            var catCount = data.CategoryCount || data.TotalCount || categories.length;
            var lineCount = data.LineCount || 0;

            // Bars scale against the largest category across the whole set (sent by the
            // server) so widths stay comparable page-to-page.
            var maxAmount = Number(data.MaxAmount || 0);
            if (maxAmount <= 0) {
                for (var m = 0; m < categories.length; m++) {
                    maxAmount = Math.max(maxAmount, Number(categories[m].Amount || 0));
                }
            }

            var subtitle = formatQty(catCount) + ' ' + msg('VAS_025_Categories', 'Categories')
                + ' · ' + formatQty(lineCount) + ' ' + msg('VAS_025_Lines', 'Lines');

            var topName = data.TopName || '';
            var topAmount = data.TopAmount != null ? Number(data.TopAmount) : 0;
            var summaryText = msg('VAS_025_ProcessedIntro', 'This month you have processed') + ' '
                + formatQty(data.InvoiceCount || 0) + ' ' + msg('VAS_025_PurchaseInvoicesLower', 'purchase invoices')
                + ' ' + msg('VAS_025_AcrossLower', 'across') + ' '
                + formatQty(catCount) + ' ' + msg('VAS_025_CategoriesLower', 'categories') + '.';
            if (topName) {
                summaryText += ' ' + msg('VAS_025_TopSpenderIs', 'The top spender is') + ' ' + topName
                    + ' ' + msg('VAS_025_AtLower', 'at') + ' ' + sym + VIS.Util.formatCompactAmount(topAmount, iso, precision) + '.';
            }

            var pageNo = data.PageNo || drillPage || 1;
            var pageCount = data.TotalPages || 1;

            var totalTip = sym + ' ' + formatFull(total, precision);
            var html = '<div class="vas-tpwidg-drill">'
                + '<div class="vas-tpwidg-drill-card">'
                +   '<span class="vas-tpwidg-drill-card-label">' + tpEsc(msg('VAS_025_TotalPurchasesMTD', 'Total Purchases')) + '</span>'
                +   '<strong class="vas-tpwidg-drill-card-value" title="' + tpEsc(totalTip) + '">' + tpEsc(sym + VIS.Util.formatCompactAmount(total, iso, precision)) + '</strong>'
                +   '<em class="vas-tpwidg-drill-card-trend">' + tpEsc(subtitle) + '</em>'
                + '</div>'
                + '<div class="vas-tpwidg-drill-copy">'
                +   '<p class="vas-tpwidg-drill-desc">' + tpEsc(summaryText) + '</p>'
                +   '<div class="vas-tpwidg-drill-rows">';

            for (var c = 0; c < categories.length; c++) {
                var category = categories[c];
                var amount = Number(category.Amount || 0);
                var width = maxAmount > 0 ? (amount / maxAmount) * 100 : 0;
                width = Math.max(0, Math.min(100, width));
                var barWidth = width > 0 ? Math.max(2, width) : 0;
                var rowTip = sym + ' ' + formatFull(amount, precision);

                html += '<div class="vas-tpwidg-drill-row">'
                    + '<div class="vas-tpwidg-drill-row-head">'
                    +   '<span class="vas-tpwidg-drill-row-name">' + tpEsc(category.IsOther ? msg('VAS_025_OtherChargesTax', 'Other (charges & tax)') : (category.Name || '-')) + '</span>'
                    +   '<span class="vas-tpwidg-drill-row-val" title="' + tpEsc(rowTip) + '">' + tpEsc(sym + VIS.Util.formatCompactAmount(amount, iso, precision)) + '</span>'
                    + '</div>'
                    + '<div class="vas-tpwidg-drill-track">'
                    +   '<div class="vas-tpwidg-drill-fill" style="width:' + barWidth.toFixed(1) + '%"></div>'
                    + '</div>'
                    + '</div>';
            }

            // Keep every popup the same (max) height: always pad the page up to
            // DRILL_PAGE_SIZE with invisible placeholder rows, so a short page or a
            // single-page popup is the same size as a full 6-row page.
            for (var pad = categories.length; pad < DRILL_PAGE_SIZE; pad++) {
                html += '<div class="vas-tpwidg-drill-row vas-tpwidg-drill-row--ph" aria-hidden="true">'
                    + '<div class="vas-tpwidg-drill-row-head"><span class="vas-tpwidg-drill-row-name">&nbsp;</span><span class="vas-tpwidg-drill-row-val">&nbsp;</span></div>'
                    + '<div class="vas-tpwidg-drill-track"></div>'
                    + '</div>';
            }

            html += '</div>';
            html += buildDrillPager(pageNo, pageCount, data.TotalCount || categories.length);
            html += '</div></div>';
            $categoryDialog.html(html);

            $categoryDialog.find('.vas-tpwidg-drill-pager-prev').on('click', function () {
                if (pageNo > 1) { loadCategoryDrilldown(pageNo - 1); }
            });
            $categoryDialog.find('.vas-tpwidg-drill-pager-next').on('click', function () {
                if (pageNo < pageCount) { loadCategoryDrilldown(pageNo + 1); }
            });

            showCategoryDialog();
        }

        /* Footer pager (rule 18): "Showing X-Y of Z" on the left, "<  n of m  >" on
           the right (24px buttons / 14px chevrons per the design spec). Page is 1-based
           (server-side); prev/next re-fetch the target page. */
        function buildDrillPager(page, pageCount, total) {
            // Always render the pager so the popup keeps a uniform height; on a single
            // page both nav buttons simply stay disabled ("1 of 1").
            if (pageCount < 1) { return ''; }
            var start = (page - 1) * DRILL_PAGE_SIZE + 1;
            var end = Math.min(page * DRILL_PAGE_SIZE, total);
            var prevDis = page <= 1 ? ' disabled' : '';
            var nextDis = page >= pageCount ? ' disabled' : '';
            var showing = msg('VAS_025_Showing', 'Showing') + ' ' + start + '–' + end
                + ' ' + msg('VAS_025_Of', 'of') + ' ' + total;
            var ofLabel = page + ' ' + msg('VAS_025_Of', 'of') + ' ' + pageCount;
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
                    drillPage = 1;
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
        // Self-wire the dashboard-width CSS variable (--dash-inline-size) the clamps read.
        ensureDashInlineSizeVar(this.getRoot());
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
