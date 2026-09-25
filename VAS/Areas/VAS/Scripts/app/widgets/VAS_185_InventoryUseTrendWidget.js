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
 *  3 | Select a month to view details         | VAS_185_ClickMonthForDetails
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
            return VIS.Msg.getMsg(key);
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

        // NOTE (2026-09-17): formatINR is called below (SVG tooltip text) but was never defined
        // anywhere in this file - a pre-existing bug that threw ReferenceError whenever a bar or
        // point was hovered. Aliased to the exact-value formatter already used for the popover.
        function formatINR(value) {
            return formatFullValue(value);
        }

        // Whole-number currency with thousands separator, no decimals - used for the
        // above-point/bar inline data labels (e.g. "₹42,000"), distinct from
        // formatCompactValue's k/L/Cr abbreviation (kept for hover tooltips) and from
        // formatFullValue's 2-decimal popover figure.
        function formatFullCurrency(value) {
            var v = Math.round(Number(value || 0));
            var sign = v < 0 ? '-' : '';
            var sym = currencySymbol || '';
            return sign + sym + Math.abs(v).toLocaleString(window.navigator.language);
        }

        // Compact, whole-number axis tick label (e.g. "₹10K", never "₹10.0k") - the
        // right (Value) axis's own formatter, separate from the data-label/tooltip ones.
        function formatAxisValue(value) {
            var v = Math.round(Number(value || 0));
            var sym = currencySymbol || '';
            if (v === 0) { return sym + '0'; }
            var sign = v < 0 ? '-' : '';
            var absV = Math.abs(v);
            if (isIndianIso(currencyIso)) {
                if (absV >= 10000000) { return sign + sym + Math.round(absV / 10000000) + 'Cr'; }
                if (absV >= 100000) { return sign + sym + Math.round(absV / 100000) + 'L'; }
                if (absV >= 1000) { return sign + sym + Math.round(absV / 1000) + 'K'; }
            } else {
                if (absV >= 1000000000) { return sign + sym + Math.round(absV / 1000000000) + 'B'; }
                if (absV >= 1000000) { return sign + sym + Math.round(absV / 1000000) + 'M'; }
                if (absV >= 1000) { return sign + sym + Math.round(absV / 1000) + 'K'; }
            }
            return sign + sym + absV.toLocaleString(window.navigator.language);
        }

        // Left (Quantity) axis tick label - whole number, thousands separator.
        function formatAxisQty(value) {
            return Math.round(Number(value || 0)).toLocaleString(window.navigator.language);
        }

        // "Nice" rounded axis max + step for tickCount intervals (standard chart-axis
        // algorithm: 1/2/5/10 x a power of ten), so gridlines land on round numbers
        // (0/500/1,000/... or 0/10K/20K/...) instead of the raw data max.
        function niceTicks(maxValue, tickCount) {
            var safeMax = Math.max(Number(maxValue) || 0, 1);
            var rawStep = safeMax / tickCount;
            var magnitude = Math.pow(10, Math.floor(Math.log(rawStep) / Math.LN10));
            var residual = rawStep / magnitude;
            var niceResidual;
            if (residual > 5) { niceResidual = 10; }
            else if (residual > 2) { niceResidual = 5; }
            else if (residual > 1) { niceResidual = 2; }
            else { niceResidual = 1; }
            var step = niceResidual * magnitude;
            var niceMax = step * tickCount;
            while (niceMax < safeMax) { niceMax += step; }
            return { max: niceMax, step: step };
        }

        function svgText(x, y, text, opts) {
            var o = opts || {};
            var t = document.createElementNS('http://www.w3.org/2000/svg', 'text');
            t.setAttribute('x', x);
            t.setAttribute('y', y);
            t.setAttribute('text-anchor', o.anchor || 'middle');
            t.setAttribute('font-size', o.size || '9');
            t.setAttribute('font-weight', o.weight || '400');
            t.setAttribute('fill', o.fill || '#5F7283');
            if (o.transform) { t.setAttribute('transform', o.transform); }
            t.textContent = text;
            return t;
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

            // Raw maxima from the data, then rounded up to "nice" axis ticks (0/500/
            // 1,000/... and 0/10K/20K/...) so both axes' gridlines land on round numbers
            // instead of the series' own max.
            var maxQtyRaw = 0;
            var maxValRaw = 0;
            for (var i = 0; i < seriesData.length; i++) {
                if (seriesData[i].qty > maxQtyRaw) { maxQtyRaw = seriesData[i].qty; }
                if (seriesData[i].val > maxValRaw) { maxValRaw = seriesData[i].val; }
            }
            var TICK_COUNT = 5;
            var qtyTicks = niceTicks(maxQtyRaw, TICK_COUNT);
            var valTicks = niceTicks(maxValRaw, TICK_COUNT);
            var maxQty = qtyTicks.max;
            var maxVal = valTicks.max;

            // Layout: [rotated title][tick labels] chart area [tick labels][rotated title]
            var leftAxisTitleW = 11;
            var leftTickLabelW = 32;
            var rightTickLabelW = 30;
            var rightAxisTitleW = 11;
            var padLeft = leftAxisTitleW + leftTickLabelW + 4;
            var padRight = rightAxisTitleW + rightTickLabelW + 4;
            var padTop = 22;
            var padBottom = 18;
            var chartW = width - padLeft - padRight;
            var chartH = height - padTop - padBottom;
            if (chartW <= 0 || chartH <= 0) { return; }

            // Horizontal gridlines (0..TICK_COUNT) + both axes' tick labels.
            for (var g = 0; g <= TICK_COUNT; g++) {
                var frac = g / TICK_COUNT;
                var gy = padTop + chartH - (chartH * frac);

                var gridLine = document.createElementNS('http://www.w3.org/2000/svg', 'line');
                gridLine.setAttribute('x1', padLeft);
                gridLine.setAttribute('y1', gy);
                gridLine.setAttribute('x2', width - padRight);
                gridLine.setAttribute('y2', gy);
                gridLine.setAttribute('stroke', '#E2EAF1');
                gridLine.setAttribute('stroke-width', '1');
                $svg.append(gridLine);

                $svg.append(svgText(padLeft - 4, gy + 3, formatAxisQty(qtyTicks.step * g), {
                    anchor: 'end', size: '9', fill: '#5F7283'
                }));
                $svg.append(svgText(width - padRight + 4, gy + 3, formatAxisValue(valTicks.step * g), {
                    anchor: 'start', size: '9', fill: '#5F7283'
                }));
            }

            // Rotated axis titles.
            var leftTitleX = leftAxisTitleW - 2;
            var midY = padTop + chartH / 2;
            $svg.append(svgText(leftTitleX, midY, label("VAS_185_Quantity", "Quantity"), {
                anchor: 'middle', size: '9', weight: '600', fill: '#41576A',
                transform: 'rotate(-90 ' + leftTitleX + ' ' + midY + ')'
            }));
            var rightTitleX = width - (rightAxisTitleW - 2);
            $svg.append(svgText(rightTitleX, midY, label("VAS_185_Value", "Value"), {
                anchor: 'middle', size: '9', weight: '600', fill: '#41576A',
                transform: 'rotate(90 ' + rightTitleX + ' ' + midY + ')'
            }));

            var numSlots = seriesData.length;
            var slotW = chartW / numSlots;
            var barW = Math.min(38, Math.max(10, slotW * 0.44));

            var points = [];
            // Candidate label positions are collected per month first and only appended to
            // the SVG after a collision pass (below) - the qty label (anchored to the bar
            // top) and the value label (anchored to the line point) both default to sitting
            // just above their own anchor, and when a month's bar height and line height are
            // proportionally close, those two "just above" positions land on top of each
            // other and render as unreadable overlapping text.
            var qtyLabelCandidates = [];
            var valLabelCandidates = [];

            // Draw Quantity Bars
            for (var j = 0; j < numSlots; j++) {
                var item = seriesData[j];
                var centerX = padLeft + (j + 0.5) * slotW;
                var barX = centerX - (barW / 2);
                var hasQty = item.qty > 0;
                var barH = hasQty ? Math.max(2, (item.qty / maxQty) * chartH) : 0;
                var barY = padTop + chartH - barH;

                // No bar drawn for a zero-qty month (matches the reference design - a flat
                // baseline with no bar, not a fake sliver). The value-line point below still
                // renders and stays clickable, so the month is never a dead zone.
                if (hasQty) {
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

                    var titleEl = document.createElementNS('http://www.w3.org/2000/svg', 'title');
                    titleEl.textContent = item.fullMonth + ': ' + label("VAS_185_Qty", "Qty") + ' ' + formatQty(item.qty)
                        + ', ' + label("VAS_185_Value", "Value") + ' ' + formatCompactValue(item.val);
                    rect.appendChild(titleEl);
                    $svg.append(rect);
                }

                // Qty inline label candidate on 3M/6M (only when there is a bar to label)
                if (selectedMonthsWindow <= 6 && hasQty) {
                    qtyLabelCandidates[j] = { x: centerX, y: Math.max(padTop - 4, barY - 4), text: formatQty(item.qty) };
                }

                // Calculate Value Line coordinates
                var valH = Math.max(2, (item.val / maxVal) * chartH);
                var valY = padTop + chartH - valH;
                points.push({ x: centerX, y: valY, item: item, idx: j });

                // Bottom Month Label
                $svg.append(svgText(centerX, height - 2, item.label, { anchor: 'middle', size: '10', fill: '#5F7283' }));
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
                titlePt.textContent = pt.item.fullMonth + ': ' + label("VAS_185_Value", "Value") + ' ' + formatCompactValue(pt.item.val);
                circle.appendChild(titlePt);
                $svg.append(circle);

                // Value inline label candidate on 3M/6M - always shown, including zero
                // (matches the reference design's "₹0" labels on empty months), full
                // currency with thousands separator rather than the compact k/L/Cr tooltip
                // format. Collected as a candidate, not appended yet - see the collision
                // pass below, which resolves overlaps against this same month's qty label.
                if (selectedMonthsWindow <= 6) {
                    var textY = (pt.y - 8 < padTop) ? pt.y + 14 : pt.y - 6;
                    valLabelCandidates[pt.idx] = { x: pt.x, y: textY, text: formatFullCurrency(pt.item.val) };
                }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//              var titlePt = document.createElementNS('http://www.w3.org/2000/svg', 'title');
//              titlePt.textContent = pt.item.fullMonth + ': Value ' + formatINR(pt.item.val);
//              circle.appendChild(titlePt);
//              $svg.append(circle);
// ----- END OLD CODE -----
            }

            // Collision pass: a month's qty label (anchored above the bar top) and value
            // label (anchored above/below the line point) are computed independently, so
            // when that month's bar height and line height are proportionally close, both
            // labels land at nearly the same Y and overlap into unreadable text. Force at
            // least MIN_LABEL_GAP of vertical separation by pushing whichever of the two is
            // already higher (smaller y) further up, leaving the lower one at its natural,
            // correctly-anchored position.
            var MIN_LABEL_GAP = 12;
            for (var m = 0; m < numSlots; m++) {
                var qtyC = qtyLabelCandidates[m];
                var valC = valLabelCandidates[m];
                if (qtyC && valC && Math.abs(valC.y - qtyC.y) < MIN_LABEL_GAP) {
                    if (qtyC.y <= valC.y) {
                        qtyC.y = valC.y - MIN_LABEL_GAP;
                    } else {
                        valC.y = qtyC.y - MIN_LABEL_GAP;
                    }
                }
            }
            for (var n = 0; n < numSlots; n++) {
                if (qtyLabelCandidates[n]) {
                    var qc = qtyLabelCandidates[n];
                    $svg.append(svgText(qc.x, qc.y, qc.text, { anchor: 'middle', size: '9', weight: '700', fill: '#0F69AC' }));
                }
                if (valLabelCandidates[n]) {
                    var vc = valLabelCandidates[n];
                    $svg.append(svgText(vc.x, vc.y, vc.text, { anchor: 'middle', size: '9', weight: '700', fill: '#9A6500' }));
                }
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
                '<div class="vas-iut-leg-hint">' + escapeHtml(label("VAS_185_ClickMonthForDetails", "Select a month to view details")) + '</div>' +
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
