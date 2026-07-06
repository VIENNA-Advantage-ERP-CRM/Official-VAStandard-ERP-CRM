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
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_094_HighestSellingProductsWidget = function () {

        this.frame;
        this.windowNo;

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
            $productName.text(row.product_name || '').attr('title', productTitle);
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
                                    '<span class="MPC-hsp-product"></span>' +
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

            $root.append($card);
            loadProducts();
        };

        this.refreshWidget = function () {
            loadProducts();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (request && request.readyState !== 4) { request.abort(); }
            if ($previousButton) { $previousButton.off('.MPCHighestSellingProducts'); }
            if ($nextButton) { $nextButton.off('.MPCHighestSellingProducts'); }
            if ($productImage) { $productImage.off('.MPCHighestSellingProducts'); }
            $root.remove();
            state.rows = [];
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
