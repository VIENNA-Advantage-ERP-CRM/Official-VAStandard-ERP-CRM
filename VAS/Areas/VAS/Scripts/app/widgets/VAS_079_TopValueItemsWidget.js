/**
 * Top Value Items Widget
 * Summary Message Table
 *  # | Current Text           | Message Key
 * ---+------------------------+--------------------------
 *  1 | Top Value Items        | VAS_TopValueItems
 *  2 | Highest carrying value | VAS_HighestCarryingValue
 *  3 | All Warehouses         | VAS_AllWarehouses
 *  4 | No stock value found.  | VAS_NoStockValueFound
 *  5 | units                  | VAS_Units
 *  6 | items                  | VAS_Items
 *  7 | of                     | VAS_Of
 *  8 | Previous page          | VAS_PreviousPage
 *  9 | Next page              | VAS_NextPage
 * 10 | Loading...             | VAS_Loading
 * 11 | Couldn't load          | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_079_TopValueItemsWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-top-value-root">');
        var $warehouseSelect;
        var $list;
        var $footer;

        var warehouses = [];
        var selectedWarehouseId = null;
        var pageNo = 1;
        var pageSize = 4;
        var totalRecords = 0;
        var currencySymbol = '';
        var currencyIso = '';
        var stdPrecision = 0;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function parseResponse(response) {
            var parsed = response;
            if (typeof parsed === 'string' && parsed) { parsed = JSON.parse(parsed); }
            if (typeof parsed === 'string' && parsed) { parsed = JSON.parse(parsed); }
            return parsed || [];
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

        function formatQty(value) {
            return formatCompactNumber(value, currencyIso);
        }

        function getPrecision(value) {
            var precision = Number(value);
            if (!isNaN(precision) && precision >= 0) { return precision; }
            if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                precision = Number(VIS.Env.getCtx().getStdPrecision());
            }
            return !isNaN(precision) && precision >= 0 ? precision : 0;
        }

        function formatAmount(value) {
            var number = Number(value || 0);
            var precision = getPrecision(stdPrecision);
            var currency = currencySymbol || currencyIso;
            var formatted = number.toLocaleString(currencyLocale(currencyIso), {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
            return currency ? currency + ' ' + formatted : formatted;
        }

        function gemIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m12 21 9-10-4-6H7l-4 6 9 10Z"></path><path d="m3 11 9 10 9-10M7 5l5 16 5-16"></path></svg>';
        }

        function renderWarehouseOptions() {
            $warehouseSelect.empty();
            $('<option>').val('').text(label('VAS_AllWarehouses', 'All Warehouses')).appendTo($warehouseSelect);
            warehouses.forEach(function (warehouse) {
                $('<option>').val(warehouse.warehouse_id).text(warehouse.warehouse_name).appendTo($warehouseSelect);
            });
            $warehouseSelect.val(selectedWarehouseId == null ? '' : String(selectedWarehouseId));
        }

        function renderItems(response) {
            var items = response.items || [];
            totalRecords = Number(response.total_records || 0);
            currencySymbol = response.currency_symbol || '';
            currencyIso = response.currency_iso || '';
            stdPrecision = response.std_precision;

            if (!items.length) {
                $list.html('<div class="MPC-tv-empty">' + escapeHtml(label('VAS_NoStockValueFound', 'No stock value found.')) + '</div>');
                renderFooter();
                return;
            }

            var html = '';
            items.forEach(function (item) {
                var amount = formatAmount(item.carrying_value);
                var warehouseName = item.warehouse_name || label('VAS_AllWarehouses', 'All Warehouses');
                html +=
                    '<button type="button" class="MPC-tv-row" data-product-id="' + Number(item.product_id) + '" data-product-name="' + escapeHtml(item.product_name) + '">' +
                        '<span class="MPC-tv-main">' +
                            '<strong>' + escapeHtml(item.product_name) + '</strong>' +
                            '<small>' + escapeHtml(formatQty(item.qty_on_hand)) + ' ' + escapeHtml(label('VAS_Units', 'units')) + ' \u00b7 ' + escapeHtml(warehouseName) + '</small>' +
                        '</span>' +
                        '<span class="MPC-tv-value" title="' + escapeHtml(amount) + '">' + escapeHtml(amount) + '</span>' +
                    '</button>';
            });
            $list.html(html);
            renderFooter();
        }

        function renderFooter() {
            var totalPages = Math.max(1, Math.ceil(totalRecords / pageSize));
            $footer.html(
                '<span>' + totalRecords.toLocaleString(window.navigator.language) + ' ' + escapeHtml(label('VAS_Items', 'items')) + '</span>' +
                '<span class="MPC-tv-pager">' +
                    '<button type="button" data-page="previous" aria-label="' + escapeHtml(label('VAS_PreviousPage', 'Previous page')) + '"' + (pageNo === 1 ? ' disabled' : '') + '>&lsaquo;</button>' +
                    '<span>' + pageNo + ' ' + escapeHtml(label('VAS_Of', 'of')) + ' ' + totalPages + '</span>' +
                    '<button type="button" data-page="next" aria-label="' + escapeHtml(label('VAS_NextPage', 'Next page')) + '"' + (pageNo === totalPages ? ' disabled' : '') + '>&rsaquo;</button>' +
                '</span>'
            );
        }

        function showError() {
            $list.html('<div class="MPC-tv-empty">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
        }

        function loadItems() {
            $list.html('<div class="MPC-tv-empty">' + escapeHtml(label('VAS_Loading', 'Loading...')) + '</div>');

            var requestData = {
                pageNo: pageNo,
                pageSize: pageSize
            };
            if (selectedWarehouseId != null) { requestData.warehouseId = selectedWarehouseId; }
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_079_TopValueItemsWidget/GetTopValueItems',
                type: 'GET',
                data: requestData,
                cache: false,
                success: function (response) {
                    var items = parseResponse(response);
                    if (items.error) { showError(); return; }
                    renderItems(items);
                },
                error: showError
            });
        }

        function loadWarehouses() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_079_TopValueItemsWidget/GetWarehouses',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = parseResponse(response);
                    if (result.error) { showError(); return; }
                    warehouses = result;
                    renderWarehouseOptions();
                    loadItems();
                },
                error: showError
            });
        }

        function createWidget() {
            var $card = $(
                '<div class="MPC-tv-card">' +
                    '<div class="MPC-tv-header">' +
                        '<span class="MPC-tv-icon">' + gemIcon() + '</span>' +
                        '<span class="MPC-tv-titles">' +
                            '<strong>' + escapeHtml(label('VAS_TopValueItems', 'Top Value Items')) + '</strong>' +
                            '<small>' + escapeHtml(label('VAS_HighestCarryingValue', 'Highest carrying value')) + '</small>' +
                        '</span>' +
                        '<select class="MPC-tv-select" aria-label="' + escapeHtml(label('Warehouse', 'Warehouse')) + '"></select>' +
                    '</div>' +
                    '<div class="MPC-tv-list"></div>' +
                    '<div class="MPC-tv-footer"></div>' +
                '</div>'
            );

            $warehouseSelect = $card.find('.MPC-tv-select');
            $list = $card.find('.MPC-tv-list');
            $footer = $card.find('.MPC-tv-footer');
            $root.append($card);

            $warehouseSelect.on('change', function () {
                var value = $(this).val();
                selectedWarehouseId = value ? Number(value) : null;
                pageNo = 1;
                loadItems();
            });

            $root.on('click', '[data-page]', function () {
                pageNo += $(this).attr('data-page') === 'next' ? 1 : -1;
                loadItems();
            });

            $root.on('click', '.MPC-tv-row', function () {
                if (!VAS.openOverallInventoryProductDetail) { return; }
                VAS.openOverallInventoryProductDetail(
                    Number($(this).attr('data-product-id')),
                    $(this).attr('data-product-name'),
                    ''
                );
            });
        }

        this.Initalize = function () {
            createWidget();
            loadWarehouses();
        };

        this.refreshWidget = function () {
            pageNo = 1;
            loadWarehouses();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_079_TopValueItemsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_079_TopValueItemsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_079_TopValueItemsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_079_TopValueItemsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
