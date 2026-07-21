/**
 * Top 10 Selling Widget (Highest / Lowest)
 * Widget number 116 - reassign on hand-off.
 * Ranked bar list of the ten best (or worst) selling products by current
 * accounting-year revenue, 5 rows per page, with a High/Low toggle. Same
 * sales/accounting-year/currency logic as VAS_094.
 * Correction 2026-07-18: clicking a product opens the Product Performance
 * modal that already exists on the VAS_094 Highest Selling Products widget
 * (same MPC-hsp-* markup/styles, same
 * VAS_094_HighestSellingProductsWidget/GetProductPerformance endpoint and
 * the same VAS_094_* message keys), replacing the old sales-detail modal.
 * Backend - VAS_116_Top10SellingWidget/GetTop10Selling
 *           VAS_094_HighestSellingProductsWidget/GetProductPerformance
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+---------------------------------------+--------------------------------
 *  1 | Top 10 Highest Selling Products       | VAS_116_Top10Highest
 *  2 | Top 10 Lowest Selling Products        | VAS_116_Top10Lowest
 *  3 | High                                  | VAS_116_High
 *  4 | Low                                   | VAS_116_Low
 *  5 | sold                                  | VAS_116_Sold
 *  6 | Showing / of                          | VAS_Showing / VAS_Of
 *  7 | No completed sales found.             | VAS_116_NoSales
 *  8 | Couldn't load                         | VAS_CouldntLoad
 *  9 | Rank                                  | VAS_116_Rank
 * 10 | Previous page / Next page             | VAS_PreviousPage / VAS_NextPage
 * 11 | Close                                 | Close
 * 12 | Product Performance modal texts       | reused VAS_094_* keys (see the
 *    |                                       | VAS_094 widget's message table)
 *    |                                       | + VAS_017_LastYear /
 *    |                                       | VAS_CurrentYear
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_116_Top10SellingWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-t10-root">');
        var $card;
        var $title;
        var $segHigh;
        var $segLow;
        var $rows;
        var $empty;
        var $footHelper;
        var $pageText;
        var $prevButton;
        var $nextButton;
        var $footer;
        var $busy;
        var request;
        var $modal;
        var $modalTitle;
        var $modalBadge;
        var $modalBody;
        var modalEventNamespace = '.MPCT10Modal';
        var eventNamespace = 'MPCTop10Selling';

        var PER_PAGE = 5;
        var state = { series: 'high', page: 0, high: [], low: [] };
        var currencySymbol = '';
        var currencyIso = '';
        var stdPrecision = 0;
        /* VAS_094 Product Performance modal state (correction 2026-07-18). */
        var modalRequest;
        var $modalBusy;
        var modalStockRows = [];
        var modalStockPage = 0;
        var MODAL_STOCK_PER_PAGE = 5;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value).replace(/[&<>"']/g, function (character) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character];
            });
        }

        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function usesIndianNumbering(iso) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(iso || '').toUpperCase()) >= 0;
        }

        function currencyLocale(iso) {
            return usesIndianNumbering(iso) ? 'en-IN' : 'en-US';
        }

        function trimZeros(text) {
            return text.replace(/\.0+$/, '').replace(/(\.\d*?)0+$/, '$1');
        }

        /* Compact number body (Indian Lakh/Crore or K/M/B) without the currency
           symbol - shared by the compact amount and the modal's compact units. */
        function compactNumber(value) {
            var number = Number(value || 0);
            var abs = Math.abs(number);
            if (usesIndianNumbering(currencyIso)) {
                if (abs >= 10000000) { return trimZeros((number / 10000000).toFixed(2)) + ' Cr'; }
                if (abs >= 100000) { return trimZeros((number / 100000).toFixed(2)) + ' L'; }
                if (abs >= 1000) { return trimZeros((number / 1000).toFixed(1)) + 'K'; }
                return number.toLocaleString('en-IN', { maximumFractionDigits: 2 });
            }
            if (abs >= 1000000000) { return trimZeros((number / 1000000000).toFixed(1)) + 'B'; }
            if (abs >= 1000000) { return trimZeros((number / 1000000).toFixed(1)) + 'M'; }
            if (abs >= 1000) { return trimZeros((number / 1000).toFixed(1)) + 'K'; }
            return number.toLocaleString('en-US', { maximumFractionDigits: 2 });
        }

        function formatCompactAmount(value) {
            return (currencySymbol || currencyIso) + ' ' + compactNumber(value);
        }

        function formatFullAmount(value) {
            var number = Number(value || 0);
            var formatted = number.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            return (currencySymbol || currencyIso) + ' ' + formatted;
        }

        function formatUnits(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        function seriesTitle() {
            return state.series === 'low'
                ? label('VAS_116_Top10Lowest', 'Top 10 Lowest Selling Products')
                : label('VAS_116_Top10Highest', 'Top 10 Highest Selling Products');
        }

        function currentRows() {
            return state.series === 'low' ? state.low : state.high;
        }

        function render() {
            $title.text(seriesTitle());
            $segHigh.toggleClass('MPC-t10-active', state.series === 'high');
            $segLow.toggleClass('MPC-t10-active', state.series === 'low');

            var rows = currentRows();
            var low = state.series === 'low';
            var total = rows.length;

            if (!total) {
                $rows.empty().addClass('MPC-t10-hidden');
                $footer.addClass('MPC-t10-hidden');
                $empty.removeClass('MPC-t10-hidden').text(label('VAS_116_NoSales', 'No completed sales found.'));
                return;
            }

            var pages = Math.ceil(total / PER_PAGE);
            if (state.page > pages - 1) { state.page = pages - 1; }
            if (state.page < 0) { state.page = 0; }

            var start = state.page * PER_PAGE;
            var end = Math.min(start + PER_PAGE, total);

            // Bar is normalized to the page max so the top row of the page reads full.
            var pageMax = 1;
            for (var m = start; m < end; m++) { pageMax = Math.max(pageMax, Number(rows[m].revenue || 0)); }

            $empty.addClass('MPC-t10-hidden');
            $rows.removeClass('MPC-t10-hidden');
            $footer.removeClass('MPC-t10-hidden');

            var html = '';
            for (var index = start; index < end; index++) {
                var row = rows[index];
                var width = Math.max(4, Math.round(Number(row.revenue || 0) / pageMax * 100));
                html +=
                    '<button type="button" class="MPC-t10-row" data-index="' + index + '">' +
                        '<span class="MPC-t10-rank">' + (index + 1) + '</span>' +
                        '<span class="MPC-t10-main">' +
                            '<span class="MPC-t10-name" title="' + escapeHtml(row.product_name) + '">' + escapeHtml(row.product_name) + '</span>' +
                            '<span class="MPC-t10-bar"><span class="MPC-t10-fill ' + (low ? 'MPC-t10-fill-low' : 'MPC-t10-fill-high') + '" style="width:' + width + '%"></span></span>' +
                        '</span>' +
                        '<span class="MPC-t10-val">' +
                            '<span class="MPC-t10-amt ' + (low ? 'MPC-t10-bad' : '') + '" title="' + escapeHtml(formatFullAmount(row.revenue)) + '">' + escapeHtml(formatCompactAmount(row.revenue)) + '</span>' +
                            '<span class="MPC-t10-units">' + escapeHtml(formatUnits(row.units) + ' ' + label('VAS_116_Sold', 'sold')) + '</span>' +
                        '</span>' +
                    '</button>';
            }
            $rows.html(html);

            $footHelper.text(label('VAS_Showing', 'Showing') + ' ' + (start + 1) + '-' + end + ' ' + label('VAS_Of', 'of') + ' ' + total);
            $pageText.text((state.page + 1) + ' ' + label('VAS_Of', 'of') + ' ' + pages);
            $prevButton.prop('disabled', state.page === 0);
            $nextButton.prop('disabled', state.page >= pages - 1);
        }

        function showError() {
            state.high = [];
            state.low = [];
            $rows.empty().addClass('MPC-t10-hidden');
            $footer.addClass('MPC-t10-hidden');
            $empty.removeClass('MPC-t10-hidden').text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        function setBusy(visible) {
            if ($busy) { $busy.toggleClass('MPC-t10-busy-hidden', !visible); }
        }

        function loadData() {
            if (request && request.readyState !== 4) { request.abort(); }

            setBusy(true);
            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_116_Top10SellingWidget/GetTop10Selling',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (!result || result.error) { showError(); return; }

                    state.page = 0;
                    state.high = result.high || [];
                    state.low = result.low || [];
                    currencySymbol = result.currency_symbol || '';
                    currencyIso = result.currency_iso || '';
                    stdPrecision = Number(result.std_precision || 0);
                    render();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                },
                complete: function () { setBusy(false); }
            });
        }

        /* ============ VAS_094 Product Performance modal ============
           Correction 2026-07-18: a product click opens the SAME modal the
           VAS_094 Highest Selling Products widget already has - identical
           MPC-hsp-* markup/styles, the same GetProductPerformance endpoint
           and the same VAS_094_* message keys. */
        function modalIcon(name) {
            var paths = {
                trend: '<polyline points="3 17 9 11 13 15 21 7"></polyline><polyline points="15 7 21 7 21 13"></polyline>',
                close: '<line x1="6" y1="6" x2="18" y2="18"></line><line x1="18" y1="6" x2="6" y2="18"></line>',
                clock: '<circle cx="12" cy="12" r="9"></circle><polyline points="12 7 12 12 15 14"></polyline>',
                chevronL: '<path d="m15 18-6-6 6-6"></path>',
                chevronR: '<path d="m9 18 6-6-6-6"></path>'
            };
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (paths[name] || '') + '</svg>';
        }

        function createModal() {
            if ($modal) { return; }

            $modal = $(
                '<div class="MPC-hsp-modal" aria-hidden="true">' +
                    '<div class="MPC-hsp-modal-scrim"></div>' +
                    '<div class="MPC-hsp-modal-dialog" role="dialog" aria-modal="true" tabindex="-1">' +
                        '<div class="MPC-hsp-modal-head">' +
                            '<span class="MPC-hsp-modal-title-wrap">' +
                                '<span class="MPC-hsp-modal-title"></span>' +
                                '<span class="MPC-hsp-modal-badge"></span>' +
                            '</span>' +
                            '<button type="button" class="MPC-hsp-modal-close">' + modalIcon('close') + '</button>' +
                        '</div>' +
                        '<div class="MPC-hsp-modal-body"></div>' +
                        '<div class="MPC-hsp-modal-busy MPC-hsp-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                    '</div>' +
                '</div>'
            );

            $modalTitle = $modal.find('.MPC-hsp-modal-title');
            $modalBadge = $modal.find('.MPC-hsp-modal-badge');
            $modalBody = $modal.find('.MPC-hsp-modal-body');
            $modalBusy = $modal.find('.MPC-hsp-modal-busy');

            var closeText = label('Close', 'Close');
            $modal.find('.MPC-hsp-modal-close').attr({ 'aria-label': closeText, title: closeText });
            $('body').append($modal);

            $modal.on('click' + modalEventNamespace, '.MPC-hsp-modal-close, .MPC-hsp-modal-scrim', closeModal);
            $(document).on('keydown' + modalEventNamespace, function (event) {
                if (event.key === 'Escape') { closeModal(); }
            });
        }

        function closeModal() {
            if (modalRequest && modalRequest.readyState !== 4) { modalRequest.abort(); }
            if (!$modal) { return; }
            $modal.removeClass('MPC-hsp-modal-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-hsp-body-lock');
        }

        function openModal(index) {
            var rows = currentRows();
            var row = rows[index];
            if (!row) { return; }
            var productId = Number(row.product_id) || 0;
            if (productId <= 0) { return; }

            createModal();
            var low = state.series === 'low';
            var rank = index + 1;

            $modalTitle.text(row.product_name || '');
            $modalBadge.html(low
                ? '<span class="MPC-hsp-pill MPC-hsp-pill-warn">' + escapeHtml(label('VAS_116_Rank', 'Rank') + ' #' + rank) + '</span>'
                : '<span class="MPC-hsp-pill MPC-hsp-pill-ok">' + escapeHtml(label('VAS_094_TopSeller', 'Top Seller') + ' · #' + rank) + '</span>');
            $modalBody.empty();
            $modalBusy.removeClass('MPC-hsp-hidden');
            $modal.addClass('MPC-hsp-modal-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-hsp-body-lock');
            $modal.find('.MPC-hsp-modal-close').trigger('focus');

            if (modalRequest && modalRequest.readyState !== 4) { modalRequest.abort(); }
            modalRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_094_HighestSellingProductsWidget/GetProductPerformance',
                type: 'GET',
                cache: false,
                data: { productId: productId },
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (!result || result.error) {
                        var reason = (result && result.error) ? result.error : label('VAS_CouldntLoad', "Couldn't load");
                        $modalBody.html('<div class="MPC-hsp-modal-state">' + escapeHtml(reason) + '</div>');
                        return;
                    }
                    renderPerformanceModal(result);
                },
                error: function (xhr, status) {
                    if (status !== 'abort') {
                        var detail = label('VAS_CouldntLoad', "Couldn't load") + ' (HTTP ' + (xhr && xhr.status ? xhr.status : 0) + ')';
                        $modalBody.html('<div class="MPC-hsp-modal-state">' + escapeHtml(detail) + '</div>');
                    }
                },
                complete: function () {
                    if ($modalBusy) { $modalBusy.addClass('MPC-hsp-hidden'); }
                }
            });
        }

        function performanceGrowth(data) {
            var lastRevenue = Number(data.last_year_revenue || 0);
            var currentRevenue = Number(data.current_year_revenue || 0);
            if (lastRevenue === 0 && currentRevenue > 0) {
                return { text: label('VAS_094_New', 'New'), tone: 'info' };
            }
            if (lastRevenue === 0) {
                return { text: '0%', tone: 'ok' };
            }
            var pct = Math.round((currentRevenue - lastRevenue) / lastRevenue * 100);
            return { text: (pct >= 0 ? '+' : '') + pct + '%', tone: pct >= 0 ? 'ok' : 'bad' };
        }

        function performanceSourcingChip(data) {
            if (data.product_type === 'S' || data.is_stocked !== 'Y') {
                return label('VAS_094_NonStockService', 'Non-Stock Service');
            }
            if (data.has_bom === 'Y') {
                return label('VAS_094_ManufacturedHasBOM', 'Manufactured · Has BOM');
            }
            return label('VAS_094_PurchasedItem', 'Purchased Item');
        }

        function performanceStockRowHtml(row, uomName) {
            return '<div class="MPC-hsp-stock-row">' +
                '<span class="MPC-hsp-stock-name" title="' + escapeHtml(row.warehouse_name || '') + '">' + escapeHtml(row.warehouse_name || '') + '</span>' +
                '<span class="MPC-hsp-stock-qty">' + escapeHtml(formatUnits(row.qty_on_hand) + ' ' + uomName) + '</span>' +
            '</div>';
        }

        function renderPerformanceStockPage(uomName) {
            var pages = Math.max(1, Math.ceil(modalStockRows.length / MODAL_STOCK_PER_PAGE));
            if (modalStockPage > pages - 1) { modalStockPage = pages - 1; }
            if (modalStockPage < 0) { modalStockPage = 0; }

            var start = modalStockPage * MODAL_STOCK_PER_PAGE;
            var end = Math.min(start + MODAL_STOCK_PER_PAGE, modalStockRows.length);
            var html = '';
            for (var index = start; index < end; index++) {
                html += performanceStockRowHtml(modalStockRows[index], uomName);
            }

            $modalBody.find('.MPC-hsp-stock-rows').html(html);
            $modalBody.find('.MPC-hsp-stock-helper').text(
                label('VAS_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + label('VAS_Of', 'of') + ' ' + modalStockRows.length
            );
            $modalBody.find('.MPC-hsp-stock-pgtext').text((modalStockPage + 1) + ' ' + label('VAS_Of', 'of') + ' ' + pages);
            $modalBody.find('.MPC-hsp-stock-prev').prop('disabled', modalStockPage === 0);
            $modalBody.find('.MPC-hsp-stock-next').prop('disabled', modalStockPage >= pages - 1);
        }

        function renderPerformanceModal(data) {
            var uomName = data.uom_name || label('VAS_094_Unit', 'Unit');
            var growth = performanceGrowth(data);
            var averagePrice = Number(data.current_year_units || 0) !== 0 ? formatFullAmount(data.avg_selling_price) : '-';

            var chips =
                '<span class="MPC-hsp-chip">' + escapeHtml(label('VAS_094_StrongPerformer', 'Strong performer')) + '</span>' +
                '<span class="MPC-hsp-chip">' + escapeHtml(label('VAS_094_UoM', 'UoM') + ' · ' + uomName) + '</span>' +
                '<span class="MPC-hsp-chip">' + escapeHtml(performanceSourcingChip(data)) + '</span>';

            var stats =
                '<div class="MPC-hsp-pstat"><span class="MPC-hsp-pstat-label">' + escapeHtml(label('VAS_094_RevenueThisYear', 'Revenue (This Year)')) + '</span><span class="MPC-hsp-pstat-value" title="' + escapeHtml(formatFullAmount(data.current_year_revenue)) + '">' + escapeHtml(formatCompactAmount(data.current_year_revenue)) + '</span></div>' +
                '<div class="MPC-hsp-pstat"><span class="MPC-hsp-pstat-label">' + escapeHtml(label('VAS_094_UnitsSold', 'Units Sold')) + '</span><span class="MPC-hsp-pstat-value">' + escapeHtml(formatUnits(data.current_year_units) + ' ' + uomName) + '</span></div>' +
                '<div class="MPC-hsp-pstat"><span class="MPC-hsp-pstat-label">' + escapeHtml(label('VAS_094_AvgSellingPrice', 'Avg Selling Price')) + '</span><span class="MPC-hsp-pstat-value">' + escapeHtml(averagePrice) + '</span></div>' +
                '<div class="MPC-hsp-pstat"><span class="MPC-hsp-pstat-label">' + escapeHtml(label('VAS_094_YoYGrowth', 'YoY Growth')) + '</span><span class="MPC-hsp-pstat-value MPC-hsp-tone-' + growth.tone + '">' + escapeHtml(growth.text) + '</span></div>';

            var hero =
                '<div class="MPC-hsp-phero">' +
                    '<span class="MPC-hsp-phero-ico">' + modalIcon('trend') + '</span>' +
                    '<span class="MPC-hsp-phero-main">' +
                        '<span class="MPC-hsp-phero-name" title="' + escapeHtml(data.product_name || '') + '">' + escapeHtml(data.product_name || '') + '</span>' +
                        '<span class="MPC-hsp-phero-chips">' + chips + '</span>' +
                    '</span>' +
                    '<span class="MPC-hsp-phero-stats">' + stats + '</span>' +
                '</div>';

            var attributesBlock = '';
            var attributeRows = data.attributes || [];
            if (attributeRows.length) {
                var attributeChips = '';
                attributeRows.forEach(function (attributeRow) {
                    attributeChips += '<span class="MPC-hsp-attr-chip"><span class="MPC-hsp-attr-label">' + escapeHtml(attributeRow.label || '') + '</span><span class="MPC-hsp-attr-value">' + escapeHtml(attributeRow.value || '') + '</span></span>';
                });
                attributesBlock =
                    '<div class="MPC-hsp-msection">' +
                        '<div class="MPC-hsp-msection-title">' + escapeHtml(label('VAS_094_Attributes', 'Attributes')) + '</div>' +
                        '<div class="MPC-hsp-attr-wrap">' + attributeChips + '</div>' +
                    '</div>';
            }

            var lastRevenue = Number(data.last_year_revenue || 0);
            var currentRevenue = Number(data.current_year_revenue || 0);
            var maxRevenue = Math.max(lastRevenue, currentRevenue, 1);
            var lastWidth = Math.max(4, Math.round(lastRevenue / maxRevenue * 100));
            var currentWidth = Math.max(4, Math.round(currentRevenue / maxRevenue * 100));

            function yoyBarHtml(labelText, width, colorClass, revenue, units) {
                return '<div class="MPC-hsp-yoy-row">' +
                    '<span class="MPC-hsp-yoy-label">' + escapeHtml(labelText) + '</span>' +
                    '<span class="MPC-hsp-yoy-track"><span class="MPC-hsp-yoy-fill ' + colorClass + '" style="width:' + width + '%"></span></span>' +
                    '<span class="MPC-hsp-yoy-value" title="' + escapeHtml(formatFullAmount(revenue)) + '">' + escapeHtml(formatCompactAmount(revenue)) + '<small>' + escapeHtml(compactNumber(units) + ' ' + uomName) + '</small></span>' +
                '</div>';
            }

            var yoyBlock =
                '<div class="MPC-hsp-msection">' +
                    '<div class="MPC-hsp-msection-title">' + escapeHtml(label('VAS_094_YearOverYear', 'Year Over Year')) + '</div>' +
                    yoyBarHtml(label('VAS_017_LastYear', 'Last Year'), lastWidth, 'MPC-hsp-yoy-last', data.last_year_revenue, data.last_year_units) +
                    yoyBarHtml(label('VAS_CurrentYear', 'Current Year'), currentWidth, 'MPC-hsp-yoy-current', data.current_year_revenue, data.current_year_units) +
                '</div>';

            modalStockRows = data.stock || [];
            modalStockPage = 0;
            var stockBlock;
            var isStockedProduct = data.is_stocked === 'Y' && data.product_type !== 'S';
            if (!isStockedProduct || !modalStockRows.length) {
                stockBlock =
                    '<div class="MPC-hsp-msection">' +
                        '<div class="MPC-hsp-msection-title">' + escapeHtml(label('VAS_094_StockOnHand', 'Stock On Hand')) + '</div>' +
                        '<div class="MPC-hsp-stock-empty">' + escapeHtml(!isStockedProduct
                            ? label('VAS_094_NonStockNoInventory', 'Non-stock item — no inventory held.')
                            : label('VAS_094_NoStockOnHand', 'No stock on hand.')) + '</div>' +
                    '</div>';
            } else {
                var totalQty = 0;
                modalStockRows.forEach(function (stockRow) { totalQty += Number(stockRow.qty_on_hand || 0); });

                var locationsText = label('VAS_094_StockOnHand', 'Stock On Hand') + ' · ' + modalStockRows.length + ' ' +
                    (modalStockRows.length === 1 ? label('VAS_094_Location', 'Location') : label('VAS_094_Locations', 'Locations'));
                var pagerBlock = '';
                if (modalStockRows.length > MODAL_STOCK_PER_PAGE) {
                    pagerBlock =
                        '<div class="MPC-hsp-stock-foot">' +
                            '<span class="MPC-hsp-stock-helper"></span>' +
                            '<span class="MPC-hsp-stock-pager">' +
                                '<button type="button" class="MPC-hsp-stock-pgbtn MPC-hsp-stock-prev" aria-label="' + escapeHtml(label('VAS_PreviousPage', 'Previous page')) + '">' + modalIcon('chevronL') + '</button>' +
                                '<span class="MPC-hsp-stock-pgtext"></span>' +
                                '<button type="button" class="MPC-hsp-stock-pgbtn MPC-hsp-stock-next" aria-label="' + escapeHtml(label('VAS_NextPage', 'Next page')) + '">' + modalIcon('chevronR') + '</button>' +
                            '</span>' +
                        '</div>';
                }

                stockBlock =
                    '<div class="MPC-hsp-msection">' +
                        '<div class="MPC-hsp-msection-title">' + escapeHtml(locationsText) + '</div>' +
                        '<div class="MPC-hsp-stock-rows"></div>' +
                        '<div class="MPC-hsp-stock-row MPC-hsp-stock-total">' +
                            '<span class="MPC-hsp-stock-name">' + escapeHtml(label('VAS_094_TotalOnHand', 'Total On Hand')) + '</span>' +
                            '<span class="MPC-hsp-stock-qty MPC-hsp-stock-qty-total">' + escapeHtml(formatUnits(totalQty) + ' ' + uomName) + '</span>' +
                        '</div>' +
                        pagerBlock +
                    '</div>';
            }

            var note =
                '<div class="MPC-hsp-mnote">' + modalIcon('clock') +
                    '<span>' + escapeHtml(label('VAS_094_TopSellerNote', 'Consider raising stock levels to avoid missed sales during peak demand.')) + '</span>' +
                '</div>';

            $modalBody.html(
                hero +
                '<div class="MPC-hsp-mcols">' +
                    '<div class="MPC-hsp-mcol">' + attributesBlock + yoyBlock + '</div>' +
                    '<div class="MPC-hsp-mcol">' + stockBlock + '</div>' +
                '</div>' +
                note
            );

            if (isStockedProduct && modalStockRows.length) {
                renderPerformanceStockPage(uomName);
                if (modalStockRows.length > MODAL_STOCK_PER_PAGE) {
                    $modalBody.find('.MPC-hsp-stock-prev').on('click' + modalEventNamespace, function () {
                        if (modalStockPage > 0) { modalStockPage--; renderPerformanceStockPage(uomName); }
                    });
                    $modalBody.find('.MPC-hsp-stock-next').on('click' + modalEventNamespace, function () {
                        modalStockPage++;
                        renderPerformanceStockPage(uomName);
                    });
                }
            }
        }

        this.Initalize = function () {
            var previousLabel = label('VAS_PreviousPage', 'Previous page');
            var nextLabel = label('VAS_NextPage', 'Next page');

            $card = $(
                '<div class="MPC-t10-card" aria-live="polite">' +
                    '<div class="MPC-t10-head">' +
                        '<span class="MPC-t10-ico" aria-hidden="true">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 17 9 11 13 15 21 7"></polyline><polyline points="14 7 21 7 21 14"></polyline></svg>' +
                        '</span>' +
                        '<span class="MPC-t10-title"></span>' +
                        '<span class="MPC-t10-spacer"></span>' +
                        '<span class="MPC-t10-toggle">' +
                            '<button type="button" class="MPC-t10-seg MPC-t10-seg-high"></button>' +
                            '<button type="button" class="MPC-t10-seg MPC-t10-seg-low"></button>' +
                        '</span>' +
                    '</div>' +
                    '<div class="MPC-t10-body">' +
                        '<div class="MPC-t10-empty MPC-t10-hidden"></div>' +
                        '<div class="MPC-t10-list"></div>' +
                        '<div class="MPC-t10-foot MPC-t10-hidden">' +
                            '<span class="MPC-t10-foot-helper"></span>' +
                            '<span class="MPC-t10-pager">' +
                                '<button type="button" class="MPC-t10-pgbtn MPC-t10-prev">' +
                                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>' +
                                '</button>' +
                                '<span class="MPC-t10-pgtext"></span>' +
                                '<button type="button" class="MPC-t10-pgbtn MPC-t10-next">' +
                                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>' +
                                '</button>' +
                            '</span>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>'
            );

            $title = $card.find('.MPC-t10-title');
            $segHigh = $card.find('.MPC-t10-seg-high').text(label('VAS_116_High', 'High'));
            $segLow = $card.find('.MPC-t10-seg-low').text(label('VAS_116_Low', 'Low'));
            $rows = $card.find('.MPC-t10-list');
            $empty = $card.find('.MPC-t10-empty');
            $footHelper = $card.find('.MPC-t10-foot-helper');
            $pageText = $card.find('.MPC-t10-pgtext');
            $footer = $card.find('.MPC-t10-foot');
            $busy = $card.find('.vis-busyindicatorouterwrap');
            $prevButton = $card.find('.MPC-t10-prev').attr({ 'aria-label': previousLabel, title: previousLabel });
            $nextButton = $card.find('.MPC-t10-next').attr({ 'aria-label': nextLabel, title: nextLabel });

            modalEventNamespace += '-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $segHigh.on('click.' + eventNamespace, function () {
                if (state.series === 'high') { return; }
                state.series = 'high';
                state.page = 0;
                render();
            });
            $segLow.on('click.' + eventNamespace, function () {
                if (state.series === 'low') { return; }
                state.series = 'low';
                state.page = 0;
                render();
            });
            $prevButton.on('click.' + eventNamespace, function () {
                if (state.page === 0) { return; }
                state.page--;
                render();
            });
            $nextButton.on('click.' + eventNamespace, function () {
                var pages = Math.ceil(currentRows().length / PER_PAGE);
                if (state.page >= pages - 1) { return; }
                state.page++;
                render();
            });
            $root.on('click.' + eventNamespace, '.MPC-t10-row', function () {
                openModal(Number($(this).attr('data-index')));
            });

            $root.append($card);
            loadData();
        };

        this.refreshWidget = function () {
            closeModal();
            loadData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            closeModal();
            if (request && request.readyState !== 4) { request.abort(); }
            $root.off('.' + eventNamespace);
            if ($segHigh) { $segHigh.off('.' + eventNamespace); }
            if ($segLow) { $segLow.off('.' + eventNamespace); }
            if ($prevButton) { $prevButton.off('.' + eventNamespace); }
            if ($nextButton) { $nextButton.off('.' + eventNamespace); }
            $(document).off('keydown' + modalEventNamespace);
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
            state.high = [];
            state.low = [];
            modalStockRows = [];
        };
    };

    VAS.VAS_116_Top10SellingWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_116_Top10SellingWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_116_Top10SellingWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_116_Top10SellingWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
