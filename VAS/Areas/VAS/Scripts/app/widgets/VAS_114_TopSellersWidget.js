/**
 * Top Sellers Widget (Highest / Lowest Selling)
 * Widget number 114 - reassign on hand-off.
 * Single-rank spotlight card: one product at a time from the sales ranking with
 * last-year vs current-year value/units, a High/Low toggle, prev/next paging and
 * product image. Same sales/accounting-year/currency logic as VAS_094.
 * Backend - VAS_114_TopSellersWidget/GetTopSellers
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+---------------------------------------+--------------------------------
 *  1 | Highest Selling Products              | VAS_114_HighestSelling
 *  2 | Lowest Selling Products               | VAS_114_LowestSelling
 *  3 | High                                  | VAS_114_High
 *  4 | Low                                   | VAS_114_Low
 *  5 | Last Year                             | VAS_017_LastYear
 *  6 | Current Year                          | VAS_CurrentYear
 *  7 | Ea                                    | VAS_114_UnitEa
 *  8 | No completed sales found.             | VAS_114_NoSales
 *  9 | Couldn't load                         | VAS_CouldntLoad
 * 10 | of                                    | VAS_Of
 * 11 | Previous / Next                       | VAS_Previous / VAS_Next
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_114_TopSellersWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-ts-root">');
        var $card;
        var $icon;
        var $title;
        var $segHigh;
        var $segLow;
        var $body;
        var $footer;
        var $pageText;
        var $prevButton;
        var $nextButton;
        var $busy;
        var request;
        var eventNamespace = 'MPCTopSellers';

        var state = { series: 'high', page: 0, high: [], low: [] };
        var currencySymbol = '';
        var currencyIso = '';
        var stdPrecision = 0;

        var ICON_HIGH = '<polyline points="3 17 9 11 13 15 21 7"></polyline><polyline points="14 7 21 7 21 14"></polyline>';
        var ICON_LOW = '<polyline points="3 7 9 13 13 9 21 17"></polyline><polyline points="14 17 21 17 21 10"></polyline>';

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value).replace(/[&<>"']/g, function (character) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character];
            });
        }

        // Joins a backend-relative image URL with the app context URL; absolute
        // http/data URLs pass through unchanged.
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

        function formatCompactNumber(value) {
            var number = Number(value || 0);
            var abs = Math.abs(number);
            if (usesIndianNumbering(currencyIso)) {
                if (abs >= 10000000) { return trimZeros((number / 10000000).toFixed(2)) + ' Cr'; }
                if (abs >= 100000) { return trimZeros((number / 100000).toFixed(2)) + ' L'; }
                if (abs >= 1000) { return trimZeros((number / 1000).toFixed(1)) + 'K'; }
            } else {
                if (abs >= 1000000000) { return trimZeros((number / 1000000000).toFixed(1)) + 'B'; }
                if (abs >= 1000000) { return trimZeros((number / 1000000).toFixed(1)) + 'M'; }
                if (abs >= 1000) { return trimZeros((number / 1000).toFixed(1)) + 'K'; }
            }
            return number.toLocaleString(currencyLocale(currencyIso), { maximumFractionDigits: 2 });
        }

        function formatCompactAmount(value) {
            return (currencySymbol || currencyIso) + formatCompactNumber(value);
        }

        function formatFullAmount(value) {
            var amount = Number(value || 0);
            var formatted = amount.toLocaleString(currencyLocale(currencyIso), {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            return (currencySymbol || currencyIso) + ' ' + formatted;
        }

        function formatUnits(value) {
            return Number(value || 0).toLocaleString(currencyLocale(currencyIso), { maximumFractionDigits: 2 });
        }

        function seriesTitle() {
            return state.series === 'low'
                ? label('VAS_114_LowestSelling', 'Lowest Selling Products')
                : label('VAS_114_HighestSelling', 'Highest Selling Products');
        }

        function valueHtml(amount, units, extraClass) {
            var eaLabel = label('VAS_114_UnitEa', 'Ea');
            return '<span class="MPC-ts-v ' + extraClass + '" title="' + escapeHtml(formatFullAmount(amount)) + '">' +
                escapeHtml(formatCompactAmount(amount)) +
                ' <small>(' + escapeHtml(formatUnits(units) + ' ' + eaLabel) + ')</small>' +
            '</span>';
        }

        function render() {
            $title.text(seriesTitle());
            $icon.html('<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' + (state.series === 'low' ? ICON_LOW : ICON_HIGH) + '</svg>');
            $segHigh.toggleClass('MPC-ts-active', state.series === 'high');
            $segLow.toggleClass('MPC-ts-active', state.series === 'low');

            var arr = state.series === 'low' ? state.low : state.high;
            var total = arr.length;
            if (!total) {
                $body.html('<div class="MPC-ts-empty">' + escapeHtml(label('VAS_114_NoSales', 'No completed sales found.')) + '</div>');
                $footer.addClass('MPC-ts-hidden');
                return;
            }

            if (state.page > total - 1) { state.page = total - 1; }
            if (state.page < 0) { state.page = 0; }
            $footer.removeClass('MPC-ts-hidden');

            var row = arr[state.page];
            var low = state.series === 'low';
            var imageUrl = resolveImageUrl(row.image_url);

            var imageHtml = imageUrl
                ? '<img class="MPC-ts-image" src="' + escapeHtml(imageUrl) + '" alt="" onerror="this.style.display=\'none\';this.previousElementSibling.style.display=\'block\';"/>'
                : '';
            var graphicHtml = '<span class="MPC-ts-graphic"' + (imageUrl ? ' style="display:none"' : '') + '>' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5z"></path><path d="M3 8l9 5 9-5"></path><line x1="12" y1="13" x2="12" y2="21"></line></svg>' +
                '</span>';

            $body.html(
                graphicHtml + imageHtml +
                '<div class="MPC-ts-head">' +
                    '<span class="MPC-ts-rank">#' + (state.page + 1) + '</span>' +
                    '<span class="MPC-ts-name" title="' + escapeHtml(row.product_name) + '">' + escapeHtml(row.product_name) + '</span>' +
                '</div>' +
                '<div class="MPC-ts-cols">' +
                    '<div class="MPC-ts-col">' +
                        valueHtml(row.previous_year_value, row.previous_year_units, 'MPC-ts-last') +
                        '<span class="MPC-ts-l">' + escapeHtml(label('VAS_017_LastYear', 'Last Year')) + '</span>' +
                    '</div>' +
                    '<div class="MPC-ts-col">' +
                        valueHtml(row.current_year_value, row.current_year_units, 'MPC-ts-cur' + (low ? ' MPC-ts-bad' : '')) +
                        '<span class="MPC-ts-l">' + escapeHtml(label('VAS_CurrentYear', 'Current Year')) + '</span>' +
                    '</div>' +
                '</div>'
            );

            $pageText.text((state.page + 1) + ' ' + label('VAS_Of', 'of') + ' ' + total);
            $prevButton.prop('disabled', state.page === 0);
            $nextButton.prop('disabled', state.page >= total - 1);
        }

        function showError() {
            state.high = [];
            state.low = [];
            $body.html('<div class="MPC-ts-empty">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
            $footer.addClass('MPC-ts-hidden');
        }

        function setBusy(visible) {
            if ($busy) { $busy.toggleClass('MPC-ts-busy-hidden', !visible); }
        }

        function loadData() {
            if (request && request.readyState !== 4) { request.abort(); }

            setBusy(true);
            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_114_TopSellersWidget/GetTopSellers',
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

        this.Initalize = function () {
            var previousLabel = label('VAS_Previous', 'Previous');
            var nextLabel = label('VAS_Next', 'Next');

            $card = $(
                '<div class="MPC-ts-card" aria-live="polite">' +
                    '<div class="MPC-ts-head-bar">' +
                        '<span class="MPC-ts-ico" aria-hidden="true"></span>' +
                        '<span class="MPC-ts-title"></span>' +
                        '<span class="MPC-ts-spacer"></span>' +
                        '<span class="MPC-ts-toggle">' +
                            '<button type="button" class="MPC-ts-seg MPC-ts-seg-high"></button>' +
                            '<button type="button" class="MPC-ts-seg MPC-ts-seg-low"></button>' +
                        '</span>' +
                    '</div>' +
                    '<div class="MPC-ts-wbody">' +
                        '<div class="MPC-ts-body"></div>' +
                        '<div class="MPC-ts-foot">' +
                            '<span class="MPC-ts-foot-spacer"></span>' +
                            '<span class="MPC-ts-pager">' +
                                '<button type="button" class="MPC-ts-pgbtn MPC-ts-prev">' +
                                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>' +
                                '</button>' +
                                '<span class="MPC-ts-pgtext"></span>' +
                                '<button type="button" class="MPC-ts-pgbtn MPC-ts-next">' +
                                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>' +
                                '</button>' +
                            '</span>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>'
            );

            $icon = $card.find('.MPC-ts-ico');
            $title = $card.find('.MPC-ts-title');
            $segHigh = $card.find('.MPC-ts-seg-high').text(label('VAS_114_High', 'High'));
            $segLow = $card.find('.MPC-ts-seg-low').text(label('VAS_114_Low', 'Low'));
            $body = $card.find('.MPC-ts-body');
            $footer = $card.find('.MPC-ts-foot');
            $pageText = $card.find('.MPC-ts-pgtext');
            $prevButton = $card.find('.MPC-ts-prev').attr({ 'aria-label': previousLabel, title: previousLabel });
            $nextButton = $card.find('.MPC-ts-next').attr({ 'aria-label': nextLabel, title: nextLabel });
            $busy = $card.find('.vis-busyindicatorouterwrap');

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
                var arr = state.series === 'low' ? state.low : state.high;
                if (state.page >= arr.length - 1) { return; }
                state.page++;
                render();
            });

            $root.append($card);
            loadData();
        };

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (request && request.readyState !== 4) { request.abort(); }
            if ($segHigh) { $segHigh.off('.' + eventNamespace); }
            if ($segLow) { $segLow.off('.' + eventNamespace); }
            if ($prevButton) { $prevButton.off('.' + eventNamespace); }
            if ($nextButton) { $nextButton.off('.' + eventNamespace); }
            $root.remove();
            state.high = [];
            state.low = [];
        };
    };

    VAS.VAS_114_TopSellersWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_114_TopSellersWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_114_TopSellersWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_114_TopSellersWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
