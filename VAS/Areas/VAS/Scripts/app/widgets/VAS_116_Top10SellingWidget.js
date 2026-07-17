/**
 * Top 10 Selling Widget (Highest / Lowest)
 * Widget number 116 - reassign on hand-off.
 * Ranked bar list of the ten best (or worst) selling products by current
 * accounting-year revenue, 5 rows per page, with a High/Low toggle and a
 * sales-detail modal. Same sales/accounting-year/currency logic as VAS_094.
 * Backend - VAS_116_Top10SellingWidget/GetTop10Selling
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
 *  9 | Sales Detail                          | VAS_116_SalesDetail
 * 10 | Rank                                  | VAS_116_Rank
 * 11 | Product                               | VAS_116_Product
 * 12 | SKU                                   | VAS_116_SKU
 * 13 | Units (current yr)                    | VAS_116_UnitsCurrentYr
 * 14 | Revenue                               | VAS_116_Revenue
 * 15 | Ea                                    | VAS_116_UnitEa
 * 16 | Previous page / Next page             | VAS_PreviousPage / VAS_NextPage
 * 17 | Close                                 | Close
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

        function formatCompactAmount(value) {
            var number = Number(value || 0);
            var abs = Math.abs(number);
            var body;
            if (usesIndianNumbering(currencyIso)) {
                if (abs >= 10000000) { body = trimZeros((number / 10000000).toFixed(2)) + ' Cr'; }
                else if (abs >= 100000) { body = trimZeros((number / 100000).toFixed(2)) + ' L'; }
                else if (abs >= 1000) { body = trimZeros((number / 1000).toFixed(1)) + 'K'; }
                else { body = number.toLocaleString('en-IN', { maximumFractionDigits: 2 }); }
            } else {
                if (abs >= 1000000000) { body = trimZeros((number / 1000000000).toFixed(1)) + 'B'; }
                else if (abs >= 1000000) { body = trimZeros((number / 1000000).toFixed(1)) + 'M'; }
                else if (abs >= 1000) { body = trimZeros((number / 1000).toFixed(1)) + 'K'; }
                else { body = number.toLocaleString('en-US', { maximumFractionDigits: 2 }); }
            }
            return (currencySymbol || currencyIso) + ' ' + body;
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

        function modalIcon(name) {
            var paths = { close: '<line x1="6" y1="6" x2="18" y2="18"></line><line x1="18" y1="6" x2="6" y2="18"></line>' };
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (paths[name] || '') + '</svg>';
        }

        function createModal() {
            if ($modal) { return; }

            $modal = $(
                '<div class="MPC-t10-modal" aria-hidden="true">' +
                    '<div class="MPC-t10-modal-scrim"></div>' +
                    '<div class="MPC-t10-modal-dialog" role="dialog" aria-modal="true" tabindex="-1">' +
                        '<div class="MPC-t10-modal-head">' +
                            '<span class="MPC-t10-modal-title-wrap">' +
                                '<span class="MPC-t10-modal-title"></span>' +
                                '<span class="MPC-t10-modal-badge"></span>' +
                            '</span>' +
                            '<button type="button" class="MPC-t10-modal-close">' + modalIcon('close') + '</button>' +
                        '</div>' +
                        '<div class="MPC-t10-modal-body"></div>' +
                    '</div>' +
                '</div>'
            );

            $modalTitle = $modal.find('.MPC-t10-modal-title');
            $modalBadge = $modal.find('.MPC-t10-modal-badge');
            $modalBody = $modal.find('.MPC-t10-modal-body');

            var closeText = label('Close', 'Close');
            $modal.find('.MPC-t10-modal-close').attr({ 'aria-label': closeText, title: closeText });
            $('body').append($modal);

            $modal.on('click' + modalEventNamespace, '.MPC-t10-modal-close, .MPC-t10-modal-scrim', closeModal);
            $(document).on('keydown' + modalEventNamespace, function (event) {
                if (event.key === 'Escape') { closeModal(); }
            });
        }

        function closeModal() {
            if (!$modal) { return; }
            $modal.removeClass('MPC-t10-modal-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-t10-body-lock');
        }

        function fieldHtml(labelText, valueText, strong) {
            return '<div class="MPC-t10-field">' +
                '<div class="MPC-t10-field-label">' + escapeHtml(labelText) + '</div>' +
                '<div class="MPC-t10-field-value' + (strong ? ' MPC-t10-strong' : '') + '">' + escapeHtml(valueText || '-') + '</div>' +
            '</div>';
        }

        function openModal(index) {
            var rows = currentRows();
            var row = rows[index];
            if (!row) { return; }

            createModal();
            var low = state.series === 'low';

            $modalTitle.text((row.product_name || '') + ' - ' + label('VAS_116_SalesDetail', 'Sales Detail'));
            $modalBadge.html('<span class="MPC-t10-pill ' + (low ? 'MPC-t10-pill-warn' : 'MPC-t10-pill-ok') + '">' +
                escapeHtml(label('VAS_116_Rank', 'Rank') + ' #' + (index + 1)) + '</span>');

            $modalBody.html(
                '<div class="MPC-t10-form-grid">' +
                    fieldHtml(label('VAS_116_Product', 'Product'), row.product_name, true) +
                    fieldHtml(label('VAS_116_SKU', 'SKU'), row.sku) +
                    fieldHtml(label('VAS_116_UnitsCurrentYr', 'Units (current yr)'), formatUnits(row.units) + ' ' + label('VAS_116_UnitEa', 'Ea')) +
                    fieldHtml(label('VAS_116_Revenue', 'Revenue'), formatFullAmount(row.revenue), true) +
                '</div>'
            );

            $modal.addClass('MPC-t10-modal-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-t10-body-lock');
            $modal.find('.MPC-t10-modal-close').trigger('focus');
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
