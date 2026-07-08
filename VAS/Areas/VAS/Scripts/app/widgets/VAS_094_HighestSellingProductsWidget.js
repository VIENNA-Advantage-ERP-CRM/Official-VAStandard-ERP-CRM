/**
 * Highest Selling Products Widget
 * Summary Message Table
 *  # | Current Text                         | Message Key
 * ---+--------------------------------------+--------------------------------
 *  1 | Highest Selling Products             | VAS_HighestSellingProduct
 *  2 | Last Year                            | VAS_017_LastYear
 *  3 | Current Year                         | VAS_CurrentYear
 *  4 | units                                | VAS_094_Units
 *  5 | No completed AR invoice sales found. | VAS_094_NoCompletedARSales
 *  6 | of                                   | VAS_Of
 *  7 | Previous                             | VAS_Previous
 *  8 | Next                                 | VAS_Next
 *  9 | Couldn't load                        | VAS_CouldntLoad
 * 10 | Attribute                            | VAS_Attribute
 * 11 | Top Seller                           | VAS_094_TopSeller
 * 12 | Strong performer                     | VAS_094_StrongPerformer
 * 13 | UoM                                  | VAS_094_UoM
 * 14 | Non-Stock Service                    | VAS_094_NonStockService
 * 15 | Manufactured · Has BOM               | VAS_094_ManufacturedHasBOM
 * 16 | Purchased Item                       | VAS_094_PurchasedItem
 * 17 | Revenue (This Year)                  | VAS_094_RevenueThisYear
 * 18 | Units Sold                           | VAS_094_UnitsSold
 * 19 | Avg Selling Price                    | VAS_094_AvgSellingPrice
 * 20 | YoY Growth                           | VAS_094_YoYGrowth
 * 21 | New                                  | VAS_094_New
 * 22 | Attributes                           | VAS_094_Attributes
 * 23 | Year Over Year                       | VAS_094_YearOverYear
 * 24 | Stock On Hand                        | VAS_094_StockOnHand
 * 25 | Location / Locations                 | VAS_094_Location / VAS_094_Locations
 * 26 | Total On Hand                        | VAS_094_TotalOnHand
 * 27 | Non-stock item — no inventory held.  | VAS_094_NonStockNoInventory
 * 28 | No stock on hand.                    | VAS_094_NoStockOnHand
 * 29 | Top-seller closing note              | VAS_094_TopSellerNote
 * 30 | Unit                                 | VAS_094_Unit
 * 31 | Showing                              | VAS_Showing
 * 32 | Previous page / Next page            | VAS_PreviousPage / VAS_NextPage
 * 33 | Close                                | Close
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_094_HighestSellingProductsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-hsp-root">');
        var $content;
        var $empty;
        var $rank;
        var $productName;
        var $productImage;
        var $graphic;
        var $productAttribute;
        var $previousValue;
        var $currentValue;
        var $pageText;
        var $previousButton;
        var $nextButton;
        var $footer;
        var $busy;
        var request;
        var modalRequest;
        var $modal;
        var $modalTitle;
        var $modalBadge;
        var $modalBody;
        var $modalBusy;
        var modalEventNamespace = '.MPCHspModal';
        var modalStockRows = [];
        var modalStockPage = 0;
        var MODAL_STOCK_PER_PAGE = 5;
        var state = { page: 0, rows: [] };
        var currencySymbol = '';
        var currencyIso = '';
        var stdPrecision = 0;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        // Joins a backend-relative image URL (e.g. "Images/1004162.png") with the
        // application context URL; absolute http/data URLs pass through unchanged.
        function resolveImageUrl(imageUrl) {
            if (!imageUrl) { return ''; }
            if (imageUrl.indexOf('http') === 0 || imageUrl.indexOf('data:') === 0) { return imageUrl; }
            var contextUrl = (VIS.Application && VIS.Application.contextUrl) || '';
            if (contextUrl && contextUrl.charAt(contextUrl.length - 1) !== '/' && imageUrl.charAt(0) !== '/') {
                return contextUrl + '/' + imageUrl;
            }
            if (contextUrl && contextUrl.charAt(contextUrl.length - 1) === '/' && imageUrl.charAt(0) === '/') {
                return contextUrl + imageUrl.substring(1);
            }
            return contextUrl + imageUrl;
        }

        // Review #8 (common): currencies of Indian-numbering countries get Indian
        // digit grouping and Lakh/Crore compact notation; all others get
        // international grouping and K/M/B. The symbol always comes from the DB.
        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function usesIndianNumbering(isoCode) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(isoCode || '').toUpperCase()) >= 0;
        }

        function currencyLocale(isoCode) {
            return usesIndianNumbering(isoCode) ? 'en-IN' : 'en-US';
        }

        function trimTrailingZeros(text) {
            return text.replace(/\.0+$/, '').replace(/(\.\d*?)0+$/, '$1');
        }

        function formatCompactNumber(value, isoCode) {
            var number = Number(value || 0);
            var abs = Math.abs(number);
            if (usesIndianNumbering(isoCode)) {
                if (abs >= 10000000) { return trimTrailingZeros((number / 10000000).toFixed(2)) + ' Cr'; }
                if (abs >= 100000) { return trimTrailingZeros((number / 100000).toFixed(2)) + ' Lakh'; }
                if (abs >= 1000) { return trimTrailingZeros((number / 1000).toFixed(1)) + 'K'; }
            } else {
                if (abs >= 1000000000) { return trimTrailingZeros((number / 1000000000).toFixed(1)) + 'B'; }
                if (abs >= 1000000) { return trimTrailingZeros((number / 1000000).toFixed(1)) + 'M'; }
                if (abs >= 1000) { return trimTrailingZeros((number / 1000).toFixed(1)) + 'K'; }
            }
            return number.toLocaleString(currencyLocale(isoCode), { maximumFractionDigits: 2 });
        }

        function formatAmount(value) {
            var amount = Number(value || 0);
            var formatted = amount.toLocaleString(currencyLocale(currencyIso), {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            return (currencySymbol || currencyIso) + ' ' + formatted;
        }

        function formatCompactAmount(value) {
            return (currencySymbol || currencyIso) + formatCompactNumber(value, currencyIso);
        }

        function formatUnits(value, compact) {
            var units = Number(value || 0);
            if (!compact) {
                return units.toLocaleString(currencyLocale(currencyIso), {
                    maximumFractionDigits: 2
                });
            }
            return formatCompactNumber(units, currencyIso);
        }

        function renderValue($element, amount, units) {
            var formattedAmount = formatAmount(amount);
            var compactAmount = formatCompactAmount(amount);
            var compactUnits = formatUnits(units, true);
            var fullUnits = formatUnits(units, false);
            var unitsLabel = label('VAS_094_Units', 'units');

            // Tile shows compact money (₹3.42 Cr / $34.2M); exact amount stays
            // in the tooltip.
            $element
                .empty()
                .attr('title', formattedAmount + ' (' + fullUnits + ' ' + unitsLabel + ')')
                .append($('<span class="MPC-hsp-amount">').text(compactAmount))
                .append($('<small class="MPC-hsp-units">').text('(' + compactUnits + ' ' + unitsLabel + ')'));
        }

        function render() {
            var total = state.rows.length;
            if (!total) {
                $content.addClass('MPC-hsp-hidden');
                $footer.addClass('MPC-hsp-hidden');
                $empty.removeClass('MPC-hsp-hidden').text(label('VAS_094_NoCompletedARSales', 'No completed AR invoice sales found.'));
                return;
            }

            if (state.page >= total) { state.page = total - 1; }
            if (state.page < 0) { state.page = 0; }

            var row = state.rows[state.page];
            $empty.addClass('MPC-hsp-hidden');
            $content.removeClass('MPC-hsp-hidden');
            $footer.removeClass('MPC-hsp-hidden');
            $rank.text('#' + (state.page + 1));

            // Review #13: rows are ranked per product + attribute, so the attribute
            // (batch/serial/variant) shows under the product name when present.
            var attribute = row.attribute || '';
            var productTitle = (row.product_name || '') + (attribute ? ' · ' + attribute : '');
            // Review point #35: only the product name opens the Product
            // Performance modal, so the name is a button carrying the ids.
            $productName
                .text(row.product_name || '')
                .attr('title', productTitle)
                .attr('data-product-id', Number(row.product_id) || 0)
                .attr('data-rank', state.page + 1);
            $productAttribute.text(attribute).attr('title', attribute).toggleClass('MPC-hsp-hidden', !attribute);

            // Review #12: the product's picture replaces the corner box graphic
            // when the backend resolved one; without an image (or when the file
            // fails to load) the original box graphic shows instead.
            var imageUrl = resolveImageUrl(row.image_url);
            if (imageUrl) {
                $productImage.attr('src', imageUrl).removeClass('MPC-hsp-hidden');
                $graphic.addClass('MPC-hsp-hidden');
            } else {
                $productImage.removeAttr('src').addClass('MPC-hsp-hidden');
                $graphic.removeClass('MPC-hsp-hidden');
            }

            renderValue($previousValue, row.previous_year_value, row.previous_year_units);
            renderValue($currentValue, row.current_year_value, row.current_year_units);
            $pageText.text((state.page + 1) + ' ' + label('VAS_Of', 'of') + ' ' + total);
            $previousButton.prop('disabled', state.page === 0);
            $nextButton.prop('disabled', state.page === total - 1);
        }

        function showError() {
            state.rows = [];
            $content.addClass('MPC-hsp-hidden');
            $footer.addClass('MPC-hsp-hidden');
            $empty.removeClass('MPC-hsp-hidden').text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        function setBusy(visible) {
            if ($busy) { $busy.toggleClass('MPC-hsp-busy-hidden', !visible); }
        }

        function loadProducts() {
            if (request && request.readyState !== 4) { request.abort(); }

            setBusy(true);
            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_094_HighestSellingProductsWidget/GetHighestSellingProducts',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (!result || result.error) {
                        showError();
                        return;
                    }

                    state.page = 0;
                    state.rows = result.rows || [];
                    currencySymbol = result.currency_symbol || '';
                    currencyIso = result.currency_iso || '';
                    stdPrecision = Number(result.std_precision || 0);
                    render();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                },
                complete: function () {
                    setBusy(false);
                }
            });
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value).replace(/[&<>"']/g, function (character) {
                return {
                    '&': '&amp;',
                    '<': '&lt;',
                    '>': '&gt;',
                    '"': '&quot;',
                    "'": '&#39;'
                }[character];
            });
        }

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

        function createPerformanceModal() {
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

            $modal.on('click' + modalEventNamespace, '.MPC-hsp-modal-close, .MPC-hsp-modal-scrim', closePerformanceModal);
            $(document).on('keydown' + modalEventNamespace, function (event) {
                if (event.key === 'Escape') { closePerformanceModal(); }
            });
        }

        function closePerformanceModal() {
            if (modalRequest && modalRequest.readyState !== 4) { modalRequest.abort(); }
            if (!$modal) { return; }
            $modal.removeClass('MPC-hsp-modal-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-hsp-body-lock');
        }

        function openPerformanceModal(productId, rank, productName) {
            createPerformanceModal();

            $modalTitle.text(productName || '');
            $modalBadge.html('<span class="MPC-hsp-pill MPC-hsp-pill-ok">' + escapeHtml(label('VAS_094_TopSeller', 'Top Seller') + ' · #' + rank) + '</span>');
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
                        $modalBody.html('<div class="MPC-hsp-modal-state">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
                        return;
                    }
                    renderPerformanceModal(result, rank);
                },
                error: function (xhr, status) {
                    if (status !== 'abort') {
                        $modalBody.html('<div class="MPC-hsp-modal-state">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
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
            // No M_Product.IsManufactured column in this schema, so assembled
            // items are recognised by IsBOM or an active M_Product_BOM row.
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
                '<span class="MPC-hsp-stock-qty">' + escapeHtml(formatUnits(row.qty_on_hand, false) + ' ' + uomName) + '</span>' +
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

        function renderPerformanceModal(data, rank) {
            var uomName = data.uom_name || label('VAS_094_Unit', 'Unit');
            var growth = performanceGrowth(data);
            var averagePrice = Number(data.current_year_units || 0) !== 0 ? formatAmount(data.avg_selling_price) : '-';

            var chips =
                '<span class="MPC-hsp-chip">' + escapeHtml(label('VAS_094_StrongPerformer', 'Strong performer')) + '</span>' +
                '<span class="MPC-hsp-chip">' + escapeHtml(label('VAS_094_UoM', 'UoM') + ' · ' + uomName) + '</span>' +
                '<span class="MPC-hsp-chip">' + escapeHtml(performanceSourcingChip(data)) + '</span>';

            var stats =
                '<div class="MPC-hsp-pstat"><span class="MPC-hsp-pstat-label">' + escapeHtml(label('VAS_094_RevenueThisYear', 'Revenue (This Year)')) + '</span><span class="MPC-hsp-pstat-value" title="' + escapeHtml(formatAmount(data.current_year_revenue)) + '">' + escapeHtml(formatCompactAmount(data.current_year_revenue)) + '</span></div>' +
                '<div class="MPC-hsp-pstat"><span class="MPC-hsp-pstat-label">' + escapeHtml(label('VAS_094_UnitsSold', 'Units Sold')) + '</span><span class="MPC-hsp-pstat-value">' + escapeHtml(formatUnits(data.current_year_units, false) + ' ' + uomName) + '</span></div>' +
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
                    '<span class="MPC-hsp-yoy-value" title="' + escapeHtml(formatAmount(revenue)) + '">' + escapeHtml(formatCompactAmount(revenue)) + '<small>' + escapeHtml(formatUnits(units, true) + ' ' + uomName) + '</small></span>' +
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
                            '<span class="MPC-hsp-stock-qty MPC-hsp-stock-qty-total">' + escapeHtml(formatUnits(totalQty, false) + ' ' + uomName) + '</span>' +
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
            var previousLabel = label('VAS_Previous', 'Previous');
            var nextLabel = label('VAS_Next', 'Next');
            var $card = $(
                '<div class="MPC-hsp-card" aria-live="polite">' +
                    '<div class="MPC-hsp-header">' +
                        '<span class="MPC-hsp-icon" aria-hidden="true">' +
                            '<svg class="MPC-hsp-icon-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                '<polyline points="3 17 9 11 13 15 21 7"></polyline>' +
                                '<polyline points="15 7 21 7 21 13"></polyline>' +
                            '</svg>' +
                        '</span>' +
                        '<span class="MPC-hsp-title"></span>' +
                    '</div>' +
                    '<div class="MPC-hsp-body">' +
                        '<div class="MPC-hsp-empty MPC-hsp-hidden"></div>' +
                        '<div class="MPC-hsp-content MPC-hsp-hidden">' +
                            '<svg class="MPC-hsp-graphic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">' +
                                '<path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path>' +
                                '<polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>' +
                                '<line x1="12" y1="22.08" x2="12" y2="12"></line>' +
                            '</svg>' +
                            '<img class="MPC-hsp-product-image MPC-hsp-hidden" alt="" />' +
                            '<div class="MPC-hsp-hero">' +
                                '<span class="MPC-hsp-rank"></span>' +
                                '<span class="MPC-hsp-product-wrap">' +
                                    '<button type="button" class="MPC-hsp-product MPC-hsp-product-btn"></button>' +
                                    '<span class="MPC-hsp-attribute MPC-hsp-hidden"></span>' +
                                '</span>' +
                            '</div>' +
                            '<div class="MPC-hsp-stats">' +
                                '<div class="MPC-hsp-stat">' +
                                    '<div class="MPC-hsp-stat-value MPC-hsp-stat-value-previous"></div>' +
                                    '<div class="MPC-hsp-stat-label MPC-hsp-previous-label"></div>' +
                                '</div>' +
                                '<div class="MPC-hsp-stat">' +
                                    '<div class="MPC-hsp-stat-value MPC-hsp-stat-value-current"></div>' +
                                    '<div class="MPC-hsp-stat-label MPC-hsp-current-label"></div>' +
                                '</div>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="MPC-hsp-footer MPC-hsp-hidden">' +
                        '<span class="MPC-hsp-footer-spacer"></span>' +
                        '<div class="MPC-hsp-pager">' +
                            '<button type="button" class="MPC-hsp-page-button MPC-hsp-previous">' +
                                '<svg class="MPC-hsp-arrow-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>' +
                            '</button>' +
                            '<span class="MPC-hsp-page-text"></span>' +
                            '<button type="button" class="MPC-hsp-page-button MPC-hsp-next">' +
                                '<svg class="MPC-hsp-arrow-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>' +
                            '</button>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>'
            );

            $card.find('.MPC-hsp-title').text(label('VAS_HighestSellingProduct', 'Highest Selling Products'));
            $card.find('.MPC-hsp-previous-label').text(label('VAS_017_LastYear', 'Last Year'));
            $card.find('.MPC-hsp-current-label').text(label('VAS_CurrentYear', 'Current Year'));
            $content = $card.find('.MPC-hsp-content');
            $empty = $card.find('.MPC-hsp-empty');
            $rank = $card.find('.MPC-hsp-rank');
            $productName = $card.find('.MPC-hsp-product');
            $productImage = $card.find('.MPC-hsp-product-image');
            $graphic = $card.find('.MPC-hsp-graphic');
            $productAttribute = $card.find('.MPC-hsp-attribute');
            $productImage.on('error.MPCHighestSellingProducts', function () {
                $(this).removeAttr('src').addClass('MPC-hsp-hidden');
                $graphic.removeClass('MPC-hsp-hidden');
            });
            $previousValue = $card.find('.MPC-hsp-stat-value-previous');
            $currentValue = $card.find('.MPC-hsp-stat-value-current');
            $pageText = $card.find('.MPC-hsp-page-text');
            $previousButton = $card.find('.MPC-hsp-previous').attr({ 'aria-label': previousLabel, title: previousLabel });
            $nextButton = $card.find('.MPC-hsp-next').attr({ 'aria-label': nextLabel, title: nextLabel });
            $footer = $card.find('.MPC-hsp-footer');
            $busy = $card.find('.vis-busyindicatorouterwrap');

            $previousButton.on('click.MPCHighestSellingProducts', function () {
                if (state.page === 0) { return; }
                state.page--;
                render();
            });

            $nextButton.on('click.MPCHighestSellingProducts', function () {
                if (state.page >= state.rows.length - 1) { return; }
                state.page++;
                render();
            });

            modalEventNamespace += '-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $root.on('click.MPCHighestSellingProducts', '.MPC-hsp-product-btn', function () {
                var productId = Number($(this).attr('data-product-id')) || 0;
                var rank = Number($(this).attr('data-rank')) || (state.page + 1);
                if (productId > 0) {
                    openPerformanceModal(productId, rank, $(this).text());
                }
            });

            $root.append($card);
            loadProducts();
        };

        this.refreshWidget = function () {
            closePerformanceModal();
            loadProducts();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            closePerformanceModal();
            if (request && request.readyState !== 4) { request.abort(); }
            if ($previousButton) { $previousButton.off('.MPCHighestSellingProducts'); }
            if ($nextButton) { $nextButton.off('.MPCHighestSellingProducts'); }
            if ($productImage) { $productImage.off('.MPCHighestSellingProducts'); }
            $root.off('.MPCHighestSellingProducts');
            $(document).off('keydown' + modalEventNamespace);
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
            state.rows = [];
            modalStockRows = [];
        };
    };

    VAS.VAS_094_HighestSellingProductsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_094_HighestSellingProductsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_094_HighestSellingProductsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_094_HighestSellingProductsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
