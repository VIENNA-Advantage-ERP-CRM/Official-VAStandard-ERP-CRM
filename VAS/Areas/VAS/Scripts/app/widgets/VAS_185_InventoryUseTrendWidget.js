/**
 * VAS_185_InventoryUseTrendWidget
 * 4x2 Chart & Info Popover Widget for Inventory Use dashboard.
 * Visualizes monthly internal-use consumption over rolling 3M/6M/12M window as a combined SVG chart
 * (blue bars for quantity, amber line for value) with inline labels on 3M/6M and click popover details.
 *
 * Summary Message Table
 *  # | Current Text                           | Message Key
 * ---+----------------------------------------+-----------------------------------
 *  1 | Inventory Use Trend                    | VAS_185_InventoryUseTrend
 *  2 | Monthly quantity and value             | VAS_185_MonthlyQuantityAndValue
 *  3 | Click a month for details              | VAS_185_ClickMonthForDetails
 *  4 | Couldn't load                           | VAS_185_CouldntLoad
 *  5 | Quantity                               | VAS_185_Quantity
 *  6 | Value                                  | VAS_185_Value
 *  7 | Documents                              | VAS_185_Documents
 *  8 | Qty                                    | VAS_185_Qty
 *
 * Month axis labels (Jan, Feb, ...) come from the CONTROLLER, not from here, and are NOT message
 * keys - no VAS widget translates month names. See
 * VAS_185_InventoryUseTrendWidgetController.MonthShortNames.
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

    VAS.VAS_185_InventoryUseTrendWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-iut-root">');
        var $card;
        var $chartWrap;
        var $svg;
        var $popover;
        var $busy;
        var $pills;

// ===== NEW CODE START — currency format (agent A07, 2026-08-19) =====
        var selectedMonthsWindow = 6;
        var seriesData = [];
        var currencyIso = '';
        var currencySymbol = '';
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//      var selectedMonthsWindow = 6;
//      var seriesData = [];
// ----- END OLD CODE -----

        function label(key, fallback) {
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
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function formatQty(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

// ===== NEW CODE START — currency format (agent A07, 2026-08-19) =====
        var INDIAN_ISOS = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function isIndianIso(iso) {
            var code = String(iso || '').toUpperCase();
            return INDIAN_ISOS.indexOf(code) >= 0;
        }

        function formatCompactValue(value) {
            var val = Number(value || 0);
            var absVal = Math.abs(val);
            var sign = val < 0 ? '-' : '';
            var sym = currencySymbol || '';

            if (typeof VIS !== 'undefined' && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                var compactStr = VIS.Util.formatCompactAmount(val, currencyIso, 1);
                return sign + sym + compactStr;
            }

            if (isIndianIso(currencyIso)) {
                if (absVal >= 10000000) {
                    return sign + sym + (absVal / 10000000).toFixed(1) + 'Cr';
                } else if (absVal >= 100000) {
                    return sign + sym + (absVal / 100000).toFixed(1) + 'L';
                } else if (absVal >= 1000) {
                    return sign + sym + (absVal / 1000).toFixed(1) + 'k';
                }
            } else {
                if (absVal >= 1000000000) {
                    return sign + sym + (absVal / 1000000000).toFixed(1) + 'B';
                } else if (absVal >= 1000000) {
                    return sign + sym + (absVal / 1000000).toFixed(1) + 'M';
                } else if (absVal >= 1000) {
                    return sign + sym + (absVal / 1000).toFixed(1) + 'k';
                }
            }
            return sign + sym + absVal.toLocaleString(window.navigator.language);
        }

        function formatFullValue(value) {
            var val = Number(value || 0);
            var sign = val < 0 ? '-' : '';
            var absVal = Math.abs(val);
            var sym = currencySymbol || '';
            return sign + sym + absVal.toLocaleString(window.navigator.language, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//      function formatINR(value) {
//          var val = Number(value || 0);
//          if (val >= 100000) {
//              return '₹' + (val / 100000).toFixed(1) + 'L';
//          } else if (val >= 1000) {
//              return '₹' + (val / 1000).toFixed(1) + 'k';
//          }
//          return '₹' + val.toLocaleString(window.navigator.language);
//      }
// ----- END OLD CODE -----

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-iut-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadTrendData();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                var ro = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                            renderChart();
                        }
                    }
                });
                ro.observe($chartWrap[0]);
            } catch (e) { }
        }

        function loadTrendData() {
            showBusy(true);
            closePopover();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_185_InventoryUseTrendWidget/GetTrendData',
                type: 'GET',
                data: { months: selectedMonthsWindow },
                cache: false,
                success: function (res) {
// ===== NEW CODE START — currency format (agent A07, 2026-08-19) =====
                    var data = parseResponse(res);
                    seriesData = data.series || [];
                    if (data.currency) {
                        currencyIso = data.currency.iso || '';
                        currencySymbol = data.currency.symbol || '';
                    }
                    renderChart();
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//                  var data = parseResponse(res);
//                  seriesData = data.series || [];
//                  renderChart();
// ----- END OLD CODE -----
                },
                error: function () {
                    seriesData = [];
                    renderChart();
                },
                complete: function () { showBusy(false); }
            });
        }

        function renderChart() {
            if (!$svg || !$chartWrap) { return; }

            var width = $chartWrap.width() || 400;
            var height = $chartWrap.height() || 180;
            if (width <= 0 || height <= 0) { return; }

            $svg.attr('viewBox', '0 0 ' + width + ' ' + height);
            $svg.empty();

            if (seriesData.length === 0) { return; }

            var padLeft = 12;
            var padRight = 12;
            var padTop = 20;
            var padBottom = 22;
            var chartW = width - padLeft - padRight;
            var chartH = height - padTop - padBottom;

            // Compute scales
            var maxQty = 1;
            var maxVal = 1;
            for (var i = 0; i < seriesData.length; i++) {
                if (seriesData[i].qty > maxQty) { maxQty = seriesData[i].qty; }
                if (seriesData[i].val > maxVal) { maxVal = seriesData[i].val; }
            }

            // Horizontal Gridlines (25%, 50%, 75%, 100%)
            for (var g = 1; g <= 4; g++) {
                var gy = padTop + chartH - (chartH * (g / 4));
                var gridLine = document.createElementNS('http://www.w3.org/2000/svg', 'line');
                gridLine.setAttribute('x1', padLeft);
                gridLine.setAttribute('y1', gy);
                gridLine.setAttribute('x2', width - padRight);
                gridLine.setAttribute('y2', gy);
                gridLine.setAttribute('stroke', '#E2EAF1');
                gridLine.setAttribute('stroke-width', '1');
                $svg.append(gridLine);
            }

            var numSlots = seriesData.length;
            var slotW = chartW / numSlots;
            var barW = Math.min(38, Math.max(10, slotW * 0.44));

            var points = [];

            // Draw Quantity Bars
            for (var j = 0; j < numSlots; j++) {
                var item = seriesData[j];
                var centerX = padLeft + (j + 0.5) * slotW;
                var barX = centerX - (barW / 2);
                var barH = Math.max(2, (item.qty / maxQty) * chartH);
                var barY = padTop + chartH - barH;

                var rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
                rect.setAttribute('x', barX);
                rect.setAttribute('y', barY);
                rect.setAttribute('width', barW);
                rect.setAttribute('height', barH);
                rect.setAttribute('rx', '3');
                rect.setAttribute('fill', '#0083DA');
                rect.setAttribute('fill-opacity', '0.85');
                rect.setAttribute('cursor', 'pointer');
                rect.setAttribute('data-idx', j);

// ===== NEW CODE START — currency format (agent A07, 2026-08-19) =====
                var titleEl = document.createElementNS('http://www.w3.org/2000/svg', 'title');
                titleEl.textContent = item.fullMonth + ': ' + label("VAS_185_Qty", "Qty") + ' ' + formatQty(item.qty)
                    + ', ' + label("VAS_185_Value", "Value") + ' ' + formatINR(item.val);
                rect.appendChild(titleEl);
                $svg.append(rect);
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//              var titleEl = document.createElementNS('http://www.w3.org/2000/svg', 'title');
//              titleEl.textContent = item.fullMonth + ': Qty ' + formatQty(item.qty) + ', Value ' + formatINR(item.val);
//              rect.appendChild(titleEl);
//              $svg.append(rect);
// ----- END OLD CODE -----

                // Qty inline label on 3M/6M
                if (selectedMonthsWindow <= 6 && item.qty > 0) {
                    var textQty = document.createElementNS('http://www.w3.org/2000/svg', 'text');
                    textQty.setAttribute('x', centerX);
                    textQty.setAttribute('y', Math.max(padTop - 4, barY - 4));
                    textQty.setAttribute('text-anchor', 'middle');
                    textQty.setAttribute('font-size', '9');
                    textQty.setAttribute('font-weight', '700');
                    textQty.setAttribute('fill', '#0F69AC');
                    textQty.textContent = formatQty(item.qty);
                    $svg.append(textQty);
                }

                // Calculate Value Line coordinates
                var valH = Math.max(2, (item.val / maxVal) * chartH);
                var valY = padTop + chartH - valH;
                points.push({ x: centerX, y: valY, item: item, idx: j });

                // Bottom Month Label
                var textMonth = document.createElementNS('http://www.w3.org/2000/svg', 'text');
                textMonth.setAttribute('x', centerX);
                textMonth.setAttribute('y', height - 4);
                textMonth.setAttribute('text-anchor', 'middle');
                textMonth.setAttribute('font-size', '10');
                textMonth.setAttribute('fill', '#5F7283');
                textMonth.textContent = item.label;
                $svg.append(textMonth);
            }

            // Draw Value Polyline
            if (points.length > 1) {
                var polyStr = '';
                for (var p = 0; p < points.length; p++) {
                    polyStr += points[p].x + ',' + points[p].y + ' ';
                }

                var poly = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
                poly.setAttribute('points', polyStr.trim());
                poly.setAttribute('fill', 'none');
                poly.setAttribute('stroke', '#D78B10');
                poly.setAttribute('stroke-width', '2');
                poly.setAttribute('stroke-linejoin', 'round');
                poly.setAttribute('stroke-linecap', 'round');
                $svg.append(poly);
            }

            // Draw Value Point Circles
            for (var c = 0; c < points.length; c++) {
                var pt = points[c];
                var circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
                circle.setAttribute('cx', pt.x);
                circle.setAttribute('cy', pt.y);
                circle.setAttribute('r', '3.5');
                circle.setAttribute('fill', '#FFFFFF');
                circle.setAttribute('stroke', '#D78B10');
                circle.setAttribute('stroke-width', '2');
                circle.setAttribute('cursor', 'pointer');
                circle.setAttribute('data-idx', pt.idx);

// ===== NEW CODE START — currency format (agent A07, 2026-08-19) =====
                var titlePt = document.createElementNS('http://www.w3.org/2000/svg', 'title');
                titlePt.textContent = pt.item.fullMonth + ': ' + label("VAS_185_Value", "Value") + ' ' + formatINR(pt.item.val);
                circle.appendChild(titlePt);
                $svg.append(circle);

                // Value inline label on 3M/6M
                if (selectedMonthsWindow <= 6 && pt.item.val > 0) {
                    var textVal = document.createElementNS('http://www.w3.org/2000/svg', 'text');
                    var textY = (pt.y - 8 < padTop) ? pt.y + 14 : pt.y - 6;
                    textVal.setAttribute('x', pt.x);
                    textVal.setAttribute('y', textY);
                    textVal.setAttribute('text-anchor', 'middle');
                    textVal.setAttribute('font-size', '9');
                    textVal.setAttribute('font-weight', '700');
                    textVal.setAttribute('fill', '#9A6500');
                    textVal.textContent = formatCompactValue(pt.item.val);
                    $svg.append(textVal);
                }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//              var titlePt = document.createElementNS('http://www.w3.org/2000/svg', 'title');
//              titlePt.textContent = pt.item.fullMonth + ': Value ' + formatINR(pt.item.val);
//              circle.appendChild(titlePt);
//              $svg.append(circle);
// ----- END OLD CODE -----
            }
        }

        /* anchorRect is the clicked bar/point in viewport coordinates. The popover lives on <body>
           and is fixed-positioned so it floats above the widget instead of being clipped by the
           card's overflow:hidden / backdrop-filter stacking context. Placement and sizing are the
           original chart-relative ones, translated into viewport coordinates. */
        function showPopover(idx, anchorRect) {
            var item = seriesData[idx];
            if (!item) { return; }

// ===== NEW CODE START — currency format (agent A07, 2026-08-19) =====
            $popover.find('.vas-iut-pop-title').text(item.fullMonth);
            $popover.find('.vas-iut-pop-qty').text(formatQty(item.qty));
            $popover.find('.vas-iut-pop-val').text(formatFullValue(item.val)).attr('title', formatFullValue(item.val) + ' (' + item.val + ')');
            $popover.find('.vas-iut-pop-docs').text(item.docs || 0);
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//          $popover.find('.vas-iut-pop-title').text(item.fullMonth);
//          $popover.find('.vas-iut-pop-qty').text(formatQty(item.qty));
//          $popover.find('.vas-iut-pop-val').text(formatINR(item.val));
//          $popover.find('.vas-iut-pop-docs').text(item.docs || 0);
// ----- END OLD CODE -----

            // Nested in the card this inherited the card's font-size; on <body> it must be copied
            // across so the em-based inner sizing renders at the original scale.
            $popover.css('font-size', $card.css('font-size'));

            // Measure after content and font-size are applied.
            $popover.css({ left: '0px', top: '0px', visibility: 'hidden' }).removeClass('vas-iut-hidden');
            var popW = $popover.outerWidth();
            var popH = $popover.outerHeight();

            var wrapRect = $chartWrap[0].getBoundingClientRect();
            var posX = anchorRect.left - wrapRect.left;
            var posY = anchorRect.top - wrapRect.top;

            var left = Math.min(posX + 10, wrapRect.width - popW - 10);
            var top = Math.max(10, Math.min(posY - 40, wrapRect.height - popH - 10));

            $popover.css({
                left: (wrapRect.left + left) + 'px',
                top: (wrapRect.top + top) + 'px',
                visibility: ''
            });
        }

        function closePopover() {
            if ($popover) { $popover.addClass('vas-iut-hidden'); }
        }

        function createWidget() {
            var title = label("VAS_185_InventoryUseTrend", "Inventory Use Trend");
            var sub = label("VAS_185_MonthlyQuantityAndValue", "Monthly quantity and value");

            $card = $(
                '<div class="vas-iut-card vas-widget-bg">' +
                '<div class="vas-iut-head">' +
                '<div class="vas-iut-head-left">' +
                '<span class="vas-iut-ico" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"></polyline><polyline points="17 6 23 6 23 12"></polyline></svg>' +
                '</span>' +
                '<div>' +
                '<div class="vas-iut-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-iut-sub">' + escapeHtml(sub) + '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-iut-switcher">' +
                '<button type="button" class="vas-iut-pill" data-m="3">3M</button>' +
                '<button type="button" class="vas-iut-pill active" data-m="6">6M</button>' +
                '<button type="button" class="vas-iut-pill" data-m="12">12M</button>' +
                '</div>' +
                '</div>' +
                '<div class="vas-iut-legend">' +
                '<div class="vas-iut-leg-item"><span class="vas-iut-swatch-bar"></span><span>' + escapeHtml(label("VAS_185_Quantity", "Quantity")) + '</span></div>' +
                '<div class="vas-iut-leg-item"><span class="vas-iut-swatch-line"></span><span>' + escapeHtml(label("VAS_185_Value", "Value")) + '</span></div>' +
                '<div class="vas-iut-leg-hint">' + escapeHtml(label("VAS_185_ClickMonthForDetails", "Click a month for details")) + '</div>' +
                '</div>' +
                '<div class="vas-iut-chart-wrap">' +
                '<svg class="vas-iut-svg"></svg>' +
                '<div class="vas-iut-popover vas-iut-hidden">' +
                '<div class="vas-iut-pop-title"></div>' +
                '<div class="vas-iut-pop-row"><span>' + escapeHtml(label("VAS_185_Quantity", "Quantity")) + ':</span><span class="vas-iut-pop-qty"></span></div>' +
                '<div class="vas-iut-pop-row"><span>' + escapeHtml(label("VAS_185_Value", "Value")) + ':</span><span class="vas-iut-pop-val"></span></div>' +
                '<div class="vas-iut-pop-row"><span>' + escapeHtml(label("VAS_185_Documents", "Documents")) + ':</span><span class="vas-iut-pop-docs"></span></div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $chartWrap = $card.find('.vas-iut-chart-wrap');
            $svg = $card.find('.vas-iut-svg');

            // Portal the popover to <body>: inside the card it is clipped by overflow:hidden and
            // trapped beneath sibling widgets by the card's backdrop-filter stacking context.
            $popover = $card.find('.vas-iut-popover').detach();
            $('body').append($popover);
            $pills = $card.find('.vas-iut-pill');

            $pills.on('click', function () {
                $pills.removeClass('active');
                $(this).addClass('active');
                selectedMonthsWindow = Number($(this).data('m') || 6);
                loadTrendData();
            });

            $svg.on('click', 'rect, circle', function (e) {
                e.stopPropagation();
                var idx = Number($(this).attr('data-idx'));
                showPopover(idx, this.getBoundingClientRect());
            });

            $(document).on('click.vas-iut', function () { closePopover(); });
            // The popover is fixed-positioned against the viewport, so it must follow scroll/resize.
            $(window).on('scroll.vas-iut resize.vas-iut', function () { closePopover(); });

            $root.append($card);

            $busy = $('<div class="vas-iut-busy vas-iut-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadTrendData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('click.vas-iut');
            $(window).off('scroll.vas-iut resize.vas-iut');
            // The popover was portaled to <body>, so it is not removed by $root.remove().
            if ($popover) { $popover.remove(); }
            $root.remove();
        };
    };

    VAS.VAS_185_InventoryUseTrendWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_185_InventoryUseTrendWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_185_InventoryUseTrendWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_185_InventoryUseTrendWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_185_InventoryUseTrendWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_185_InventoryUseTrendWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
