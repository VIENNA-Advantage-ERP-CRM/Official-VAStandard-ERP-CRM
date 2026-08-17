/**
 * VAS_196_MonthlyPOTrendWidget
 * 4x2 Column Chart Widget for Purchase Order Dashboard.
 * Visualizes up to 12 months of Purchase Order value by DateOrdered with month bars
 * opening the matching PO drill-down list and lines.
 *
 * Summary Message Table
 *  #  | Current Text                           | Message Key
 * ----+----------------------------------------+-----------------------------------
 *  1  | Monthly Purchase Order Trend           | VAS_MonthlyPurchaseOrderTrend
 *  2  | PO value in                            | VAS_POValueIn
 *  3  | months                                 | VAS_Months
 *  4  | Range limited to 12 months             | VAS_RangeLimited12Months
 *  5  | Purchase Orders                        | VAS_PurchaseOrders
 *  6  | All POs raised in the selected month   | VAS_AllPOsRaisedInSelectedMonth
 *  7  | PO count                               | VAS_POCount
 *  8  | PO value                               | VAS_POValue
 *  9  | Vendors                                | VAS_Vendors
 *  10 | Avg PO value                           | VAS_AvgPOValue
 *  11 | PO No                                  | VAS_PONo
 *  12 | PO date                                | VAS_PODate
 *  13 | Vendor                                 | VAS_Vendor
 *  14 | Warehouse                              | VAS_Warehouse
 *  15 | Representative                         | VAS_Representative
 *  16 | Value                                  | VAS_Value
 *  17 | Delivery                               | VAS_Delivery
 *  18 | Status                                 | VAS_Status
 *  19 | Lines                                  | VAS_Lines
 *  20 | Product                                | VAS_Product
 *  21 | Attribute                              | VAS_Attribute
 *  22 | UoM                                    | VAS_UOM
 *  23 | Ordered                                | VAS_Ordered
 *  24 | Received                               | VAS_Received
 *  25 | Pending                                | VAS_Pending
 *  26 | Rate                                   | VAS_Rate
 *  27 | Amount                                 | VAS_Amount
 *  28 | Line status                            | VAS_LineStatus
 *  29 | Close                                  | Close
 *  30 | Back                                   | Back
 *  31 | Showing                                | Showing
 *  32 | of                                     | Of
 *  33 | No purchase orders found for this month| VAS_NoPOsFoundForMonth
 *  34 | Purchase order lines                   | VAS_PurchaseOrderLines
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    function lbl(key, fallback) {
        var translated = VIS.Msg.getMsg(key);
        return (translated && translated.charAt(0) !== '[') ? translated : fallback;
    }

    function escapeHtml(value) {
        return String(value == null ? "" : value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function parseResponse(res) {
        var data = res;
        if (typeof data === 'string') {
            try { data = JSON.parse(data); } catch (e) { }
        }
        if (typeof data === 'string') {
            try { data = JSON.parse(data); } catch (e) { }
        }
        return data || {};
    }

    var MONTH_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    // 12-entry calendar month pastel color palette (indexed by month - 1)
    var MONTH_TINTS = [
        ['#A9D2FF', '#7FB9F5'], // Jan - Blue
        ['#A3E0D4', '#5CC4AE'], // Feb - Teal
        ['#FFDCA1', '#F0BC66'], // Mar - Amber
        ['#CFC9F5', '#A79EE8'], // Apr - Lilac
        ['#FFC7C7', '#F09A9A'], // May - Rose
        ['#BEE9CD', '#84D3A4'], // Jun - Green
        ['#CFE8FF', '#93C6F2'], // Jul - Sky
        ['#FFE9B8', '#EFC978'], // Aug - Sand/Gold
        ['#C8F0DF', '#8FDCC0'], // Sep - Mint
        ['#E3D3F7', '#BFA6EA'], // Oct - Soft Purple
        ['#FFD6C2', '#F5AE8C'], // Nov - Peach
        ['#D7E3EE', '#A9BFD3']  // Dec - Slate
    ];

    VAS.VAS_196_MonthlyPOTrendWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-mpt-root">');
        var $card;
        var $fromSel, $toSel;
        var $subLabel;
        var $plot, $axis;
        var $busy;
        var $toast;

        var now = new Date();
        var curYear = now.getFullYear();
        var curMonth = now.getMonth() + 1; // 1-12

        var minIdx = 2024 * 12; // Jan 2024
        var maxIdx = curYear * 12 + (curMonth - 1) + 4; // 4 months past current
        var toIdx = curYear * 12 + (curMonth - 1);
        var fromIdx = toIdx - 11; // 12 months rolling window

        var trendSeries = [];
        var currencyInfo = { symbol: '₹', iso: 'INR', precision: 2 };

        // Modal engine state
        var $mask = null;
        var $modal = null;
        var modalHistoryStack = [];
        var currentModalCfg = null;

        function idxToYearMonth(idx) {
            var y = Math.floor(idx / 12);
            var m = (idx % 12) + 1;
            return { year: y, month: m };
        }

        function idxToLabel(idx) {
            var ym = idxToYearMonth(idx);
            return MONTH_SHORT[ym.month - 1] + ' ' + ym.year;
        }

        function formatCompactMoney(val, curSym) {
            var sym = curSym || currencyInfo.symbol || '';
            var v = Number(val || 0);
            if (v >= 1e7) {
                return sym + ' ' + (v / 1e7).toFixed(2) + ' Cr';
            }
            if (v >= 1e5) {
                return sym + ' ' + (v / 1e5).toFixed(2) + ' L';
            }
            if (v >= 1e3) {
                return sym + ' ' + (v / 1e3).toFixed(1) + ' k';
            }
            return sym + ' ' + v.toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        function formatNumber(val) {
            var n = Number(val || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-mpt-hidden', !show);
        }

        function showToast(msg) {
            if (!$toast) {
                $toast = $('<div class="vas-mpt-toast"></div>');
                $('body').append($toast);
            }
            $toast.text(msg).addClass('vas-mpt-toast-show');
            clearTimeout($toast._timer);
            $toast._timer = setTimeout(function () {
                $toast.removeClass('vas-mpt-toast-show');
            }, 2600);
        }

        function populateSelectOptions($sel, selectedVal) {
            var html = '';
            for (var i = minIdx; i <= maxIdx; i++) {
                html += '<option value="' + i + '"' + (i === selectedVal ? ' selected' : '') + '>' + escapeHtml(idxToLabel(i)) + '</option>';
            }
            $sel.html(html);
        }

        function buildWidget() {
            var title = lbl("VAS_MonthlyPurchaseOrderTrend", "Monthly Purchase Order Trend");

            $card = $(
                '<div class="vas-mpt-card">' +
                    '<div class="vas-mpt-head">' +
                        '<div class="vas-mpt-head-txt">' +
                            '<p class="vas-mpt-title">' + escapeHtml(title) + '</p>' +
                            '<p class="vas-mpt-sub"></p>' +
                        '</div>' +
                        '<div class="vas-mpt-filter">' +
                            '<select class="vas-mpt-sel vas-mpt-from" aria-label="From month"></select>' +
                            '<span class="vas-mpt-arrow">→</span>' +
                            '<select class="vas-mpt-sel vas-mpt-to" aria-label="To month"></select>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-mpt-colchart">' +
                        '<div class="vas-mpt-plot"></div>' +
                        '<div class="vas-mpt-axis"></div>' +
                    '</div>' +
                '</div>'
            );

            $fromSel = $card.find('.vas-mpt-from');
            $toSel = $card.find('.vas-mpt-to');
            $subLabel = $card.find('.vas-mpt-sub');
            $plot = $card.find('.vas-mpt-plot');
            $axis = $card.find('.vas-mpt-axis');

            populateSelectOptions($fromSel, fromIdx);
            populateSelectOptions($toSel, toIdx);

            $fromSel.on('change', function () { handleFilterChange('from'); });
            $toSel.on('change', function () { handleFilterChange('to'); });

            $plot.on('click', '.vas-mpt-colwrap', function () {
                var year = parseInt($(this).attr('data-year'), 10);
                var month = parseInt($(this).attr('data-month'), 10);
                if (year && month) {
                    openMonthPODrilldown(year, month);
                }
            });

            $root.append($card);

            $busy = $('<div class="vas-mpt-busy vas-mpt-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        function handleFilterChange(changedSource) {
            fromIdx = parseInt($fromSel.val(), 10);
            toIdx = parseInt($toSel.val(), 10);
            var clamped = false;

            if (fromIdx > toIdx) {
                if (changedSource === 'from') {
                    toIdx = fromIdx;
                } else {
                    fromIdx = toIdx;
                }
                clamped = true;
            }

            if (toIdx - fromIdx > 11) {
                if (changedSource === 'from') {
                    toIdx = fromIdx + 11;
                    if (toIdx > maxIdx) {
                        toIdx = maxIdx;
                        fromIdx = toIdx - 11;
                    }
                } else {
                    fromIdx = toIdx - 11;
                }
                clamped = true;
            }

            populateSelectOptions($fromSel, fromIdx);
            populateSelectOptions($toSel, toIdx);

            if (clamped) {
                showToast(lbl("VAS_RangeLimited12Months", "Range limited to 12 months"));
            }

            loadTrendData();
        }

        function loadTrendData() {
            showBusy(true);

            var fromYM = idxToYearMonth(fromIdx);
            var toYM = idxToYearMonth(toIdx);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_196_MonthlyPOTrendWidget/GetTrendData',
                type: 'GET',
                data: {
                    fromYear: fromYM.year,
                    fromMonth: fromYM.month,
                    toYear: toYM.year,
                    toMonth: toYM.month
                },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data && data.series) {
                        trendSeries = data.series;
                        if (data.currency) {
                            currencyInfo = data.currency;
                        }
                    } else {
                        trendSeries = [];
                    }
                    renderChart();
                },
                error: function () {
                    trendSeries = [];
                    renderChart();
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderChart() {
            if (!$plot || !$axis) { return; }

            var curSym = currencyInfo.symbol || '₹';
            var fromLabel = idxToLabel(fromIdx);
            var toLabel = idxToLabel(toIdx);
            var monthCount = trendSeries.length || 1;

            var subTxt = fromLabel + ' – ' + toLabel + ' · ' + monthCount + ' ' + lbl("VAS_Months", "months") + ' · ' + lbl("VAS_POValueIn", "PO value in") + ' ' + curSym;
            $subLabel.text(subTxt);

            if (!trendSeries || trendSeries.length === 0) {
                $plot.html('<div class="vas-mpt-empty">' + escapeHtml(lbl("VAS_NoPOsFoundForMonth", "No purchase orders found for this period")) + '</div>');
                $axis.empty();
                return;
            }

            // Find maximum value to scale columns
            var maxVal = 0;
            for (var i = 0; i < trendSeries.length; i++) {
                if (trendSeries[i].value > maxVal) {
                    maxVal = trendSeries[i].value;
                }
            }

            var scaleCeiling = (maxVal > 0 ? maxVal : 1) * 1.18;

            var plotHtml = '';
            var axisHtml = '';

            for (var j = 0; j < trendSeries.length; j++) {
                var item = trendSeries[j];
                var mColorIdx = (item.month - 1) % 12;
                var palette = MONTH_TINTS[mColorIdx];
                var heightPct = maxVal > 0 ? Math.round((item.value / scaleCeiling) * 100) : 0;
                if (item.value > 0 && heightPct < 4) { heightPct = 4; } // minimum legible height

                var valDisplay = item.value >= 1e7 ? (item.value / 1e7).toFixed(1) + 'Cr' :
                                 item.value >= 1e5 ? (item.value / 1e5).toFixed(1) + 'L' :
                                 item.value >= 1e3 ? (item.value / 1e3).toFixed(0) + 'k' :
                                 item.value > 0 ? Math.round(item.value).toString() : '0';

                var tip = item.label + ' — ' + formatCompactMoney(item.value, curSym);

                plotHtml +=
                    '<button type="button" class="vas-mpt-colwrap" data-year="' + item.year + '" data-month="' + item.month + '" title="' + escapeHtml(tip) + '">' +
                        '<span class="vas-mpt-colval">' + escapeHtml(valDisplay) + '</span>' +
                        '<span class="vas-mpt-col" style="height:' + heightPct + '%; background:' + palette[0] + '; border-top-color:' + palette[1] + '"></span>' +
                    '</button>';

                axisHtml += '<span title="' + escapeHtml(item.label) + '">' + escapeHtml(item.shortName) + '</span>';
            }

            $plot.html(plotHtml);
            $axis.html(axisHtml);
        }

        /* ============================================================
           MODAL / DRILL-DOWN SYSTEM
           ============================================================ */

        function ensureModalShell() {
            if ($mask && $mask[0]) { return; }

            $mask = $(
                '<div class="vas-mpt-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-mpt-modal">' +
                        '<div class="vas-mpt-modal-head">' +
                            '<div class="vas-mpt-modal-head-left">' +
                                '<button type="button" class="vas-mpt-xbtn vas-mpt-back-btn" aria-label="' + escapeHtml(lbl("Back", "Back")) + '" style="display:none;">' +
                                    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>' +
                                '</button>' +
                                '<div class="vas-mpt-modal-htxt">' +
                                    '<h2 class="vas-mpt-modal-title"></h2>' +
                                    '<div class="vas-mpt-modal-msub"></div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-mpt-modal-hact">' +
                                '<button type="button" class="vas-mpt-xbtn vas-mpt-close-btn" aria-label="' + escapeHtml(lbl("Close", "Close")) + '">' +
                                    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>' +
                                '</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-mpt-modal-body"></div>' +
                        '<div class="vas-mpt-modal-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $modal = $mask.find('.vas-mpt-modal');

            $mask.find('.vas-mpt-close-btn').on('click', closeModal);
            $mask.find('.vas-mpt-back-btn').on('click', backModal);
            $mask.on('click', function (e) {
                if (e.target === $mask[0]) { closeModal(); }
            });

            $(document).on('keydown.vas-mpt', function (e) {
                if (e.key === 'Escape' && $mask.hasClass('vas-mpt-mask-open')) {
                    closeModal();
                }
            });

            $('body').append($mask);
        }

        function openModalView(cfg, isBackNav) {
            ensureModalShell();

            if (!isBackNav) {
                if (cfg.isChild && currentModalCfg) {
                    modalHistoryStack.push(currentModalCfg);
                } else if (!cfg.isChild) {
                    modalHistoryStack = [];
                }
                currentModalCfg = cfg;
            }

            var hasBack = modalHistoryStack.length > 0;
            $mask.find('.vas-mpt-back-btn').toggle(hasBack);

            $modal.toggleClass('vas-mpt-modal-md', !!cfg.isMedium);
            $modal.find('.vas-mpt-modal-title').text(cfg.title || '');
            $modal.find('.vas-mpt-modal-msub').text(cfg.subtitle || '');
            $modal.find('.vas-mpt-modal-body').html(cfg.body || '');
            $modal.find('.vas-mpt-modal-foot').html(cfg.foot || '<span class="vas-mpt-foot-note"></span><button type="button" class="vas-mpt-btn vas-mpt-close-action">' + escapeHtml(lbl("Close", "Close")) + '</button>');

            $modal.find('.vas-mpt-close-action').on('click', closeModal);

            if (cfg.onRender) {
                cfg.onRender($modal);
            }

            $mask.addClass('vas-mpt-mask-open');
        }

        function backModal() {
            var prevCfg = modalHistoryStack.pop();
            if (!prevCfg) {
                closeModal();
                return;
            }
            currentModalCfg = prevCfg;
            openModalView(prevCfg, true);
        }

        function closeModal() {
            if ($mask) {
                $mask.removeClass('vas-mpt-mask-open');
            }
            modalHistoryStack = [];
            currentModalCfg = null;
        }

        function zoomToPurchaseOrder(orderId) {
            if (!orderId) { return; }
            try {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_Order.C_Order_ID=" + orderId,
                    "TabLayout": "Y",
                    "TabIndex": "0",
                    "AD_Tab_ID": 1002398,
                    "ActionName": "VAS_PurchaseOrder",
                    "ActionType": "W"
                });
            } catch (e) {
                if (VIS && VIS.viewManager && VIS.viewManager.startWindow) {
                    VIS.viewManager.startWindow("VAS_PurchaseOrder", "C_Order.C_Order_ID=" + orderId);
                }
            }
        }

        /* Drill-down: Month PO List */
        function openMonthPODrilldown(year, month) {
            var monthLabel = MONTH_SHORT[month - 1] + ' ' + year;
            var title = lbl("VAS_PurchaseOrders", "Purchase Orders") + ' — ' + monthLabel;
            var sub = lbl("VAS_AllPOsRaisedInSelectedMonth", "All POs raised in the selected month");

            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_196_MonthlyPOTrendWidget/GetMonthPODrilldown',
                type: 'GET',
                data: { year: year, month: month },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var records = data.records || [];
                    var poCount = data.poCount || records.length;
                    var poVal = data.poValue || 0;
                    var vendorCount = data.vendorCount || 0;
                    var avgPoVal = data.avgPoValue || 0;
                    var sym = (data.currency && data.currency.symbol) ? data.currency.symbol : currencyInfo.symbol;

                    renderMonthPOModal(title, sub, monthLabel, records, poCount, poVal, vendorCount, avgPoVal, sym, year, month);
                },
                error: function () {
                    showToast(lbl("Error", "Error loading data"));
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderMonthPOModal(title, subtitle, monthLabel, records, poCount, poVal, vendorCount, avgPoVal, curSym, year, month) {
            var PAGE_SIZE = 10;
            var curPage = 0;
            var totalPages = Math.max(1, Math.ceil(records.length / PAGE_SIZE));

            function buildBodyHtml() {
                var statsHtml =
                    '<div class="vas-mpt-mstats">' +
                        '<div class="vas-mpt-mstat"><div class="l">' + escapeHtml(lbl("VAS_POCount", "PO count")) + '</div><div class="v">' + formatNumber(poCount) + '</div></div>' +
                        '<div class="vas-mpt-mstat"><div class="l">' + escapeHtml(lbl("VAS_POValue", "PO value")) + '</div><div class="v">' + formatCompactMoney(poVal, curSym) + '</div></div>' +
                        '<div class="vas-mpt-mstat"><div class="l">' + escapeHtml(lbl("VAS_Vendors", "Vendors")) + '</div><div class="v">' + formatNumber(vendorCount) + '</div></div>' +
                        '<div class="vas-mpt-mstat"><div class="l">' + escapeHtml(lbl("VAS_AvgPOValue", "Avg PO value")) + '</div><div class="v">' + formatCompactMoney(avgPoVal, curSym) + '</div></div>' +
                    '</div>' +
                    '<div class="vas-mpt-msec">' + escapeHtml(lbl("VAS_PurchaseOrders", "Purchase Orders")) + '</div>' +
                    '<div class="vas-mpt-mtwrap" id="vas-mpt-table-wrap">' +
                        '<div class="vas-mpt-mtbl">' +
                            '<div class="vas-mpt-mrow vas-mpt-mhead" style="grid-template-columns: minmax(0, 0.32fr) minmax(0, 1.2fr) minmax(0, 1fr) minmax(0, 1.7fr) minmax(0, 1.2fr) minmax(0, 1.2fr) minmax(0, 0.9fr) minmax(0, 1.05fr) minmax(0, 1.1fr);">' +
                                '<span class="vas-mpt-cell center"></span>' +
                                '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_PONo", "PO No")) + '">' + escapeHtml(lbl("VAS_PONo", "PO No")) + '</span>' +
                                '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_PODate", "PO date")) + '">' + escapeHtml(lbl("VAS_PODate", "PO date")) + '</span>' +
                                '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_Vendor", "Vendor")) + '">' + escapeHtml(lbl("VAS_Vendor", "Vendor")) + '</span>' +
                                '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_Warehouse", "Warehouse")) + '">' + escapeHtml(lbl("VAS_Warehouse", "Warehouse")) + '</span>' +
                                '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_Representative", "Representative")) + '">' + escapeHtml(lbl("VAS_Representative", "Representative")) + '</span>' +
                                '<span class="vas-mpt-cell right" title="' + escapeHtml(lbl("VAS_Value", "Value")) + '">' + escapeHtml(lbl("VAS_Value", "Value")) + '</span>' +
                                '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_Delivery", "Delivery")) + '">' + escapeHtml(lbl("VAS_Delivery", "Delivery")) + '</span>' +
                                '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_Status", "Status")) + '">' + escapeHtml(lbl("VAS_Status", "Status")) + '</span>' +
                            '</div>' +
                            '<div class="vas-mpt-mbody" id="vas-mpt-rows-container"></div>' +
                        '</div>' +
                        '<div class="vas-mpt-mtfoot" id="vas-mpt-foot-container"></div>' +
                    '</div>';
                return statsHtml;
            }

            function renderRows($modalRoot) {
                var start = curPage * PAGE_SIZE;
                var slice = records.slice(start, start + PAGE_SIZE);
                var rowsHtml = '';

                if (slice.length === 0) {
                    rowsHtml = '<div class="vas-mpt-empty" style="padding: 2em 0;">' + escapeHtml(lbl("VAS_NoPOsFoundForMonth", "No purchase orders found for this month")) + '</div>';
                } else {
                    for (var i = 0; i < slice.length; i++) {
                        var p = slice[i];
                        var poValFormatted = formatCompactMoney(p.value, curSym);
                        rowsHtml +=
                            '<div class="vas-mpt-mrow" style="grid-template-columns: minmax(0, 0.32fr) minmax(0, 1.2fr) minmax(0, 1fr) minmax(0, 1.7fr) minmax(0, 1.2fr) minmax(0, 1.2fr) minmax(0, 0.9fr) minmax(0, 1.05fr) minmax(0, 1.1fr);">' +
                                '<span class="vas-mpt-cell center"><button type="button" class="vas-mpt-iconbtn vas-mpt-lines-btn" data-id="' + p.orderId + '" data-no="' + escapeHtml(p.poNo) + '" title="' + escapeHtml(lbl("VAS_Lines", "Lines")) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg></button></span>' +
                                '<span class="vas-mpt-cell"><button type="button" class="vas-mpt-lnk vas-mpt-zoom-btn" data-id="' + p.orderId + '" title="' + escapeHtml(p.poNo) + '">' + escapeHtml(p.poNo) + '</button></span>' +
                                '<span class="vas-mpt-cell vas-mpt-c-std" title="' + escapeHtml(p.orderDateFormatted) + '">' + escapeHtml(p.orderDateFormatted) + '</span>' +
                                '<span class="vas-mpt-cell vas-mpt-c-std" title="' + escapeHtml(p.vendor) + '">' + escapeHtml(p.vendor) + '</span>' +
                                '<span class="vas-mpt-cell vas-mpt-c-std" title="' + escapeHtml(p.warehouse) + '">' + escapeHtml(p.warehouse) + '</span>' +
                                '<span class="vas-mpt-cell vas-mpt-c-std" title="' + escapeHtml(p.rep) + '">' + escapeHtml(p.rep) + '</span>' +
                                '<span class="vas-mpt-cell right vas-mpt-c-emph" title="' + escapeHtml(poValFormatted) + '">' + escapeHtml(poValFormatted) + '</span>' +
                                '<span class="vas-mpt-cell"><span class="vas-mpt-chip ' + p.deliveryChip + '" title="' + escapeHtml(p.deliveryStatus) + '">' + escapeHtml(p.deliveryStatus) + '</span></span>' +
                                '<span class="vas-mpt-cell"><span class="vas-mpt-chip ' + p.statusChip + '" title="' + escapeHtml(p.statusLabel) + '">' + escapeHtml(p.statusLabel) + '</span></span>' +
                            '</div>';
                    }
                }

                $modalRoot.find('#vas-mpt-rows-container').html(rowsHtml);

                var showingTxt = records.length === 0 ? '' :
                    lbl("Showing", "Showing") + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' + lbl("of", "of") + ' ' + records.length;

                var footHtml =
                    '<span class="helper">' + escapeHtml(showingTxt) + '</span>' +
                    (totalPages > 1 ?
                        '<span class="vas-mpt-pager">' +
                            '<button type="button" class="vas-mpt-pbtn vas-mpt-prev-btn"' + (curPage === 0 ? ' disabled' : '') + '><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                            '<span class="vas-mpt-ptxt">' + (curPage + 1) + ' ' + lbl("of", "of") + ' ' + totalPages + '</span>' +
                            '<button type="button" class="vas-mpt-pbtn vas-mpt-next-btn"' + (curPage >= totalPages - 1 ? ' disabled' : '') + '><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                        '</span>' : '<span></span>');

                $modalRoot.find('#vas-mpt-foot-container').html(footHtml);

                // Wire action clicks
                $modalRoot.find('.vas-mpt-zoom-btn').on('click', function () {
                    var orderId = parseInt($(this).attr('data-id'), 10);
                    zoomToPurchaseOrder(orderId);
                });

                $modalRoot.find('.vas-mpt-lines-btn').on('click', function () {
                    var orderId = parseInt($(this).attr('data-id'), 10);
                    var poNo = $(this).attr('data-no');
                    openPOLinesDrilldown(orderId, poNo, curSym);
                });

                $modalRoot.find('.vas-mpt-prev-btn').on('click', function () {
                    if (curPage > 0) {
                        curPage--;
                        renderRows($modalRoot);
                    }
                });

                $modalRoot.find('.vas-mpt-next-btn').on('click', function () {
                    if (curPage < totalPages - 1) {
                        curPage++;
                        renderRows($modalRoot);
                    }
                });
            }

            openModalView({
                isChild: false,
                isMedium: false,
                title: title,
                subtitle: subtitle,
                body: buildBodyHtml(),
                foot: '<span class="vas-mpt-foot-note">' + records.length + ' ' + lbl("VAS_PurchaseOrders", "purchase orders") + '</span><button type="button" class="vas-mpt-btn vas-mpt-close-action">' + escapeHtml(lbl("Close", "Close")) + '</button>',
                onRender: function ($m) {
                    renderRows($m);
                }
            });
        }

        /* Drill-down: PO Lines child modal */
        function openPOLinesDrilldown(orderId, poNo, curSym) {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_196_MonthlyPOTrendWidget/GetPOLineDetails',
                type: 'GET',
                data: { orderId: orderId },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var lines = data.lines || [];
                    renderPOLinesModal(orderId, poNo, lines, curSym);
                },
                error: function () {
                    showToast(lbl("Error", "Error loading line details"));
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderPOLinesModal(orderId, poNo, lines, curSym) {
            var title = lbl("VAS_Lines", "Lines") + ' · ' + poNo;
            var sub = lbl("VAS_PurchaseOrderLines", "Purchase order lines");
            var PAGE_SIZE = 10;
            var curPage = 0;
            var totalPages = Math.max(1, Math.ceil(lines.length / PAGE_SIZE));

            var totalOrdered = 0;
            var totalPending = 0;
            var totalAmt = 0;
            for (var k = 0; k < lines.length; k++) {
                totalOrdered += (lines[k].qtyOrdered || 0);
                totalPending += (lines[k].qtyPending || 0);
                totalAmt += (lines[k].amount || 0);
            }

            function buildLinesBodyHtml() {
                return '<div class="vas-mpt-mstats">' +
                    '<div class="vas-mpt-mstat"><div class="l">' + escapeHtml(lbl("VAS_Lines", "Lines")) + '</div><div class="v">' + lines.length + '</div></div>' +
                    '<div class="vas-mpt-mstat"><div class="l">' + escapeHtml(lbl("VAS_Ordered", "Qty ordered")) + '</div><div class="v">' + formatNumber(totalOrdered) + '</div></div>' +
                    '<div class="vas-mpt-mstat"><div class="l">' + escapeHtml(lbl("VAS_Pending", "Qty pending")) + '</div><div class="v">' + formatNumber(totalPending) + '</div></div>' +
                    '<div class="vas-mpt-mstat"><div class="l">' + escapeHtml(lbl("VAS_POValue", "PO value")) + '</div><div class="v">' + formatCompactMoney(totalAmt, curSym) + '</div></div>' +
                '</div>' +
                '<div class="vas-mpt-msec">' + escapeHtml(lbl("VAS_PurchaseOrderLines", "Purchase order lines")) + '</div>' +
                '<div class="vas-mpt-mtwrap">' +
                    '<div class="vas-mpt-mtbl">' +
                        '<div class="vas-mpt-mrow vas-mpt-mhead" style="grid-template-columns: minmax(0, 0.3fr) minmax(0, 1.5fr) minmax(0, 1.2fr) minmax(0, 0.5fr) minmax(0, 0.7fr) minmax(0, 0.7fr) minmax(0, 0.7fr) minmax(0, 0.7fr) minmax(0, 0.9fr) minmax(0, 1fr);">' +
                            '<span class="vas-mpt-cell right">#</span>' +
                            '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_Product", "Product")) + '">' + escapeHtml(lbl("VAS_Product", "Product")) + '</span>' +
                            '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_Attribute", "Attribute")) + '">' + escapeHtml(lbl("VAS_Attribute", "Attribute")) + '</span>' +
                            '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_UOM", "UoM")) + '">' + escapeHtml(lbl("VAS_UOM", "UoM")) + '</span>' +
                            '<span class="vas-mpt-cell right" title="' + escapeHtml(lbl("VAS_Ordered", "Ordered")) + '">' + escapeHtml(lbl("VAS_Ordered", "Ordered")) + '</span>' +
                            '<span class="vas-mpt-cell right" title="' + escapeHtml(lbl("VAS_Received", "Received")) + '">' + escapeHtml(lbl("VAS_Received", "Received")) + '</span>' +
                            '<span class="vas-mpt-cell right" title="' + escapeHtml(lbl("VAS_Pending", "Pending")) + '">' + escapeHtml(lbl("VAS_Pending", "Pending")) + '</span>' +
                            '<span class="vas-mpt-cell right" title="' + escapeHtml(lbl("VAS_Rate", "Rate")) + '">' + escapeHtml(lbl("VAS_Rate", "Rate")) + '</span>' +
                            '<span class="vas-mpt-cell right" title="' + escapeHtml(lbl("VAS_Amount", "Amount")) + '">' + escapeHtml(lbl("VAS_Amount", "Amount")) + '</span>' +
                            '<span class="vas-mpt-cell" title="' + escapeHtml(lbl("VAS_LineStatus", "Line status")) + '">' + escapeHtml(lbl("VAS_LineStatus", "Line status")) + '</span>' +
                        '</div>' +
                        '<div class="vas-mpt-mbody" id="vas-mpt-line-rows-container"></div>' +
                    '</div>' +
                    '<div class="vas-mpt-mtfoot" id="vas-mpt-line-foot-container"></div>' +
                '</div>';
            }

            function renderLineRows($mRoot) {
                var start = curPage * PAGE_SIZE;
                var slice = lines.slice(start, start + PAGE_SIZE);
                var html = '';

                if (slice.length === 0) {
                    html = '<div class="vas-mpt-empty" style="padding: 2em 0;">' + escapeHtml(lbl("VAS_NoPOsFoundForMonth", "No lines found")) + '</div>';
                } else {
                    for (var i = 0; i < slice.length; i++) {
                        var l = slice[i];
                        html +=
                            '<div class="vas-mpt-mrow" style="grid-template-columns: minmax(0, 0.3fr) minmax(0, 1.5fr) minmax(0, 1.2fr) minmax(0, 0.5fr) minmax(0, 0.7fr) minmax(0, 0.7fr) minmax(0, 0.7fr) minmax(0, 0.7fr) minmax(0, 0.9fr) minmax(0, 1fr);">' +
                                '<span class="vas-mpt-cell right vas-mpt-c-std">' + l.lineNo + '</span>' +
                                '<span class="vas-mpt-cell vas-mpt-c-prim" title="' + escapeHtml(l.product) + '">' + escapeHtml(l.product) + '</span>' +
                                '<span class="vas-mpt-cell vas-mpt-c-std" title="' + escapeHtml(l.attribute) + '">' + escapeHtml(l.attribute) + '</span>' +
                                '<span class="vas-mpt-cell vas-mpt-c-std" title="' + escapeHtml(l.uom) + '">' + escapeHtml(l.uom) + '</span>' +
                                '<span class="vas-mpt-cell right" title="' + formatNumber(l.qtyOrdered) + '">' + formatNumber(l.qtyOrdered) + '</span>' +
                                '<span class="vas-mpt-cell right" title="' + formatNumber(l.qtyDelivered) + '">' + formatNumber(l.qtyDelivered) + '</span>' +
                                '<span class="vas-mpt-cell right vas-mpt-c-prim" title="' + formatNumber(l.qtyPending) + '">' + formatNumber(l.qtyPending) + '</span>' +
                                '<span class="vas-mpt-cell right" title="' + formatNumber(l.rate) + '">' + curSym + ' ' + formatNumber(l.rate) + '</span>' +
                                '<span class="vas-mpt-cell right vas-mpt-c-emph" title="' + formatCompactMoney(l.amount, curSym) + '">' + formatCompactMoney(l.amount, curSym) + '</span>' +
                                '<span class="vas-mpt-cell"><span class="vas-mpt-chip ' + l.statusChip + '" title="' + escapeHtml(l.status) + '">' + escapeHtml(l.status) + '</span></span>' +
                            '</div>';
                    }
                }

                $mRoot.find('#vas-mpt-line-rows-container').html(html);

                var showingTxt = lines.length === 0 ? '' :
                    lbl("Showing", "Showing") + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' + lbl("of", "of") + ' ' + lines.length;

                var footHtml =
                    '<span class="helper">' + escapeHtml(showingTxt) + '</span>' +
                    (totalPages > 1 ?
                        '<span class="vas-mpt-pager">' +
                            '<button type="button" class="vas-mpt-pbtn vas-mpt-l-prev"' + (curPage === 0 ? ' disabled' : '') + '><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                            '<span class="vas-mpt-ptxt">' + (curPage + 1) + ' ' + lbl("of", "of") + ' ' + totalPages + '</span>' +
                            '<button type="button" class="vas-mpt-pbtn vas-mpt-l-next"' + (curPage >= totalPages - 1 ? ' disabled' : '') + '><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                        '</span>' : '<span></span>');

                $mRoot.find('#vas-mpt-line-foot-container').html(footHtml);

                $mRoot.find('.vas-mpt-l-prev').on('click', function () {
                    if (curPage > 0) {
                        curPage--;
                        renderLineRows($mRoot);
                    }
                });

                $mRoot.find('.vas-mpt-l-next').on('click', function () {
                    if (curPage < totalPages - 1) {
                        curPage++;
                        renderLineRows($mRoot);
                    }
                });
            }

            openModalView({
                isChild: true,
                isMedium: true,
                title: title,
                subtitle: sub,
                body: buildLinesBodyHtml(),
                foot: '<span class="vas-mpt-foot-note">' + poNo + '</span><span><button type="button" class="vas-mpt-btn vas-mpt-back-action" style="margin-right:8px;">' + escapeHtml(lbl("Back", "Back")) + '</button><button type="button" class="vas-mpt-btn vas-mpt-close-action">' + escapeHtml(lbl("Close", "Close")) + '</button></span>',
                onRender: function ($m) {
                    $m.find('.vas-mpt-back-action').on('click', backModal);
                    renderLineRows($m);
                }
            });
        }

        /* Lifecycle methods */
        this.Initalize = function () {
            buildWidget();
            loadTrendData();
        };

        this.refreshWidget = function () {
            loadTrendData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            closeModal();
            if ($toast) { $toast.remove(); $toast = null; }
            if ($mask) { $mask.remove(); $mask = null; }
            $(document).off('keydown.vas-mpt');
            $root.remove();
        };
    };

    VAS.VAS_196_MonthlyPOTrendWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_196_MonthlyPOTrendWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_196_MonthlyPOTrendWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_196_MonthlyPOTrendWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_196_MonthlyPOTrendWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_196_MonthlyPOTrendWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
    };

})(VAS, jQuery);
