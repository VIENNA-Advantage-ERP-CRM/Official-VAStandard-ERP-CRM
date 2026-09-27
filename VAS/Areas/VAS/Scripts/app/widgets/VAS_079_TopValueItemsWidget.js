/**
 * Top Value Items Widget
 * Summary Message Table
 *  # | Current Text           | Message Key
 * ---+------------------------+--------------------------
 *  1 | Top Value Items        | VAS_079_TopValueItems
 *  2 | Highest carrying value | VAS_079_HighestCarryingValue
 *  3 | All Warehouses         | VAS_079_AllWarehouses
 *  4 | No stock value found.  | VAS_079_NoStockValueFound
 *  5 | units                  | VAS_079_Units
 *  6 | items                  | VAS_079_Items
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
        var loading = false;
        var rowResizeObserver = null;

        var $self = this;
        // Product Master zoom target (Home-page fallback) - same window names VAS_078
        // uses, cached after the first resolve so later clicks skip the lookup.
        var ZOOM_WINDOW_NAME_NEW = 'VAS_ProductMaster';
        var ZOOM_WINDOW_NAME_OLD = 'Product';
        var productWindowId = 0;

        function label(key, fallback) {
            return VIS.Msg.getMsg(key);
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

        // ===== NEW CODE START — currency format (2026-09-08) =====
        // Money is rendered in the organization's ISO 4217 currency: Indian-numbering
        // currencies get Indian grouping with Lakh/Crore, every other code gets
        // international grouping with K/M/B. No unnecessary decimals, the symbol always
        // comes from the endpoint payload (never hardcoded), null/empty renders as 0.
        function currencyPrefix() {
            var symbol = currencySymbol || currencyIso;
            if (!symbol) { return ''; }
            // Multi-character symbols ("ID", "Rp") need a separator; "₹0" / "$0" do not.
            return symbol.length > 1 ? symbol + ' ' : symbol;
        }

        function toAmountNumber(value) {
            var number = Number(value);
            return isFinite(number) ? number : 0;
        }

        function formatAmount(value) {
            var number = toAmountNumber(value);
            var sign = number < 0 ? '-' : '';
            return sign + currencyPrefix() + formatCompactNumber(Math.abs(number), currencyIso);
        }

        // Exact, unabbreviated amount for the hover title, so the compact display never
        // hides the precise figure. Honours the currency's StdPrecision (IQD needs 3).
        function formatAmountExact(value) {
            var number = toAmountNumber(value);
            var sign = number < 0 ? '-' : '';
            return sign + currencyPrefix() + Math.abs(number).toLocaleString(currencyLocale(currencyIso), {
                minimumFractionDigits: 0,
                maximumFractionDigits: getPrecision(stdPrecision)
            });
        }
        // ===== NEW CODE END — currency format =====

        // ----- OLD CODE (kept for rollback, do not delete) -----
        // function formatAmount(value) {
        //     var number = Number(value || 0);
        //     var precision = getPrecision(stdPrecision);
        //     var currency = currencySymbol || currencyIso;
        //     var formatted = number.toLocaleString(currencyLocale(currencyIso), {
        //         minimumFractionDigits: precision,
        //         maximumFractionDigits: precision
        //     });
        //     return currency ? currency + ' ' + formatted : formatted;
        // }
        // ----- END OLD CODE -----

        function gemIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m12 21 9-10-4-6H7l-4 6 9 10Z"></path><path d="m3 11 9 10 9-10M7 5l5 16 5-16"></path></svg>';
        }

        function renderWarehouseOptions() {
            $warehouseSelect.empty();
            $('<option>').val('').text(label('VAS_079_AllWarehouses', 'All Warehouses')).appendTo($warehouseSelect);
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
                $list.html('<div class="MPC-tv-empty">' + escapeHtml(label('VAS_079_NoStockValueFound', 'No stock value found.')) + '</div>');
                renderFooter();
                return;
            }

            var html = '';
            items.forEach(function (item) {
                var amount = formatAmount(item.carrying_value);
                var exactAmount = formatAmountExact(item.carrying_value);
                var warehouseName = item.warehouse_name || label('VAS_079_AllWarehouses', 'All Warehouses');
                html +=
                    '<button type="button" class="MPC-tv-row" data-product-id="' + Number(item.product_id) + '" data-product-name="' + escapeHtml(item.product_name) + '">' +
                        '<span class="MPC-tv-main">' +
                            '<strong>' + escapeHtml(item.product_name) + '</strong>' +
                            '<small>' + escapeHtml(formatQty(item.qty_on_hand)) + ' ' + escapeHtml(label('VAS_079_Units', 'units')) + ' \u00b7 ' + escapeHtml(warehouseName) + '</small>' +
                        '</span>' +
                        '<span class="MPC-tv-value" title="' + escapeHtml(exactAmount) + '">' + escapeHtml(amount) + '</span>' +
                    '</button>';
            });
            $list.html(html);
            renderFooter();
        }

        function renderFooter() {
            var totalPages = Math.max(1, Math.ceil(totalRecords / pageSize));
            $footer.html(
                '<span>' + totalRecords.toLocaleString(window.navigator.language) + ' ' + escapeHtml(label('VAS_079_Items', 'items')) + '</span>' +
                '<span class="MPC-tv-pager">' +
                    '<button type="button" data-page="previous" aria-label="' + escapeHtml(label('VAS_PreviousPage', 'Previous page')) + '"' + (pageNo === 1 ? ' disabled' : '') + '>&lsaquo;</button>' +
                    '<span>' + pageNo + ' ' + escapeHtml(label('VAS_Of', 'of')) + ' ' + totalPages + '</span>' +
                    '<button type="button" data-page="next" aria-label="' + escapeHtml(label('VAS_NextPage', 'Next page')) + '"' + (pageNo === totalPages ? ' disabled' : '') + '>&rsaquo;</button>' +
                '</span>'
            );
        }

        function measurePageSize() {
            if (!$list || !$list[0]) { return pageSize; }

            var listHeight = $list.innerHeight();
            var rowHeight = $list.find('.MPC-tv-row').first().outerHeight(true) || 50;
            if (!listHeight || !rowHeight) { return pageSize; }

            return Math.max(3, Math.floor(listHeight / rowHeight));
        }

        function syncPageSize() {
            var nextPageSize = measurePageSize();
            if (nextPageSize === pageSize) { return; }

            var firstRecord = ((pageNo - 1) * pageSize) + 1;
            pageSize = nextPageSize;
            pageNo = Math.max(1, Math.ceil(firstRecord / pageSize));
            if (!loading) { loadItems(); }
        }

        function showError() {
            $list.html('<div class="MPC-tv-empty">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
            loading = false;
        }

        function loadItems() {
            loading = true;
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
                    loading = false;
                    var items = parseResponse(response);
                    if (items.error) { showError(); return; }
                    renderItems(items);
                    window.setTimeout(syncPageSize, 0);
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

        /* Navigate to the product record on the Product Master screen. Self-contained
           (does not depend on VAS_078's widget instance being present on the same
           dashboard - see the row click handler below) so this keeps working whether
           or not the Product Search widget is also on this page.
             - Hosted inside a window (windowNo >= 0): fire the host's value-changed
               channel with a TabWhereClause; the host re-queries the window it is
               ALREADY in and switches to single/form layout.
             - Home / Landing page: VAS.ZoomUtil.zoomToRecord opens (or reuses) the
               Product Master window directly. */
        function zoomProductRecord(productId) {
            var recordId = Number(productId || 0);
            if (recordId <= 0) { return; }

            if ($self.windowNo >= 0) {
                try {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": "M_Product.M_Product_ID=" + recordId,
                        "TabLayout": "Y",
                        "TabIndex": "0"
                    });
                } catch (e) { }
                return;
            }

            if (!window.VAS || !VAS.ZoomUtil || typeof VAS.ZoomUtil.zoomToRecord !== 'function') { return; }
            VAS.ZoomUtil.zoomToRecord('M_Product_ID', recordId, productWindowId,
                                      ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                .done(function (id) {
                    if (id > 0) { productWindowId = id; }
                });
        }

        function createWidget() {
            var $card = $(
                '<div class="MPC-tv-card">' +
                    '<div class="MPC-tv-header">' +
                        '<span class="MPC-tv-icon">' + gemIcon() + '</span>' +
                        '<span class="MPC-tv-titles">' +
                            '<strong>' + escapeHtml(label('VAS_079_TopValueItems', 'Top Value Items')) + '</strong>' +
                            '<small>' + escapeHtml(label('VAS_079_HighestCarryingValue', 'Highest carrying value')) + '</small>' +
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
                var productId = Number($(this).attr('data-product-id'));
                // Prefer the richer Product Detail modal when VAS_078 (Product Search)
                // is also on this dashboard and has initialized; otherwise this row's
                // click used to silently no-op (the global is only defined while that
                // OTHER widget's instance is alive). zoomProductRecord is self-contained
                // and always works, on a window or on the Home page.
                if (VAS.openOverallInventoryProductDetail) {
                    VAS.openOverallInventoryProductDetail(
                        productId,
                        $(this).attr('data-product-name'),
                        ''
                    );
                    return;
                }
                zoomProductRecord(productId);
            });

            if (window.ResizeObserver) {
                rowResizeObserver = new ResizeObserver(function () {
                    window.setTimeout(syncPageSize, 0);
                });
                rowResizeObserver.observe($list[0]);
            }
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
            if (rowResizeObserver) { rowResizeObserver.disconnect(); rowResizeObserver = null; }
            $root.remove();
        };
    };

    VAS.VAS_079_TopValueItemsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_079_TopValueItemsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
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
