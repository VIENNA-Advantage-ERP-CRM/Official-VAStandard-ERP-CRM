/**
 * Warehouse Stock Widget
 * Summary Message Table
 *  # | Current Text                         | Message Key
 * ---+--------------------------------------+-------------------------------
 *  1 | Warehouse Stock                      | VAS_WarehouseStock
 *  2 | Locator                              | VAS_Locator
 *  3 | Qty                                  | VAS_Qty
 *  4 | Value                                | VAS_Value
 *  5 | locators                             | VAS_Locators
 *  6 | Showing                              | VAS_Showing
 *  7 | of                                   | VAS_Of
 *  8 | Inventory Value by Age               | VAS_InventoryValueByAge
 *  9 | 0-30 days                            | VAS_Age0To30Days
 * 10 | 31-90 days                           | VAS_Age31To90Days
 * 11 | 91-365 days                          | VAS_Age91To365Days
 * 12 | >365 days                            | VAS_AgeOver365Days
 * 13 | of value                             | VAS_OfValue
 * 14 | total carrying value in this locator | VAS_TotalCarryingValueLocator
 * 15 | units                                | VAS_Units
 * 16 | No stock found for this warehouse.   | VAS_NoWarehouseStock
 * 17 | Previous page                        | VAS_PreviousPage
 * 18 | Next page                            | VAS_NextPage
 * 19 | Close                                | Close
 * 20 | Couldn't load                        | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_080_WarehouseStockWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-warehouse-stock-root">');
        var $card;
        var $warehouseSelect;
        var $summary;
        var $list;
        var $footer;
        var $modal;
        var $modalBadge;
        var $modalBody;

        var warehouseStockState = {
            warehouseId: null,
            page: 1,
            pageSize: 4,
            rows: [],
            warehouses: [],
            currencySymbol: '',
            currencyIso: '',
            stdPrecision: 0
        };

        var ageBands = [
            { key: 'value_0_30', messageKey: 'VAS_Age0To30Days', fallback: '0\u201330 days', className: 'MPC-ws-band-0' },
            { key: 'value_31_90', messageKey: 'VAS_Age31To90Days', fallback: '31\u201390 days', className: 'MPC-ws-band-1' },
            { key: 'value_91_365', messageKey: 'VAS_Age91To365Days', fallback: '91\u2013365 days', className: 'MPC-ws-band-2' },
            { key: 'value_over_365', messageKey: 'VAS_AgeOver365Days', fallback: '>365 days', className: 'MPC-ws-band-3' }
        ];

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
        // digit grouping; all others get international grouping. The symbol
        // always comes from the DB.
        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function usesIndianNumbering(isoCode) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(isoCode || '').toUpperCase()) >= 0;
        }

        function currencyLocale(isoCode) {
            return usesIndianNumbering(isoCode) ? 'en-IN' : 'en-US';
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(currencyLocale(warehouseStockState.currencyIso), { maximumFractionDigits: 2 });
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
            var precision = getPrecision(warehouseStockState.stdPrecision);
            var currency = warehouseStockState.currencySymbol || warehouseStockState.currencyIso;
            var formatted = number.toLocaleString(currencyLocale(warehouseStockState.currencyIso), {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
            return currency ? currency + ' ' + formatted : formatted;
        }

        function getSelectedWarehouseRows() {
            return warehouseStockState.rows.filter(function (row) {
                return Number(row.warehouse_id) === Number(warehouseStockState.warehouseId);
            });
        }

        function getSummary(rows) {
            var summary = { locators: rows.length, quantity: 0, value: 0 };
            rows.forEach(function (row) {
                summary.quantity += Number(row.total_qty || 0);
                summary.value += Number(row.total_value || 0);
            });
            return summary;
        }

        function getAgeShares(row) {
            var values = ageBands.map(function (band) {
                return Math.max(0, Number(row[band.key] || 0));
            });
            var total = values.reduce(function (sum, value) { return sum + value; }, 0);
            return values.map(function (value) { return total > 0 ? value * 100 / total : 0; });
        }

        function renderStackedBar(shares, className) {
            var offset = 0;
            var rects = ageBands.map(function (band, index) {
                var width = Math.max(0, shares[index]);
                var rect = '<rect class="' + band.className + '" x="' + offset.toFixed(2) + '" y="0" width="' + width.toFixed(2) + '" height="1"></rect>';
                offset += width;
                return rect;
            }).join('');
            return '<svg class="' + className + '" viewBox="0 0 100 1" preserveAspectRatio="none" aria-hidden="true">' + rects + '</svg>';
        }

        function renderBandBar(share, band) {
            return '<svg viewBox="0 0 100 1" preserveAspectRatio="none" aria-hidden="true"><rect class="' + band.className + '" x="0" y="0" width="' + Math.max(0, share).toFixed(2) + '" height="1"></rect></svg>';
        }

        function warehouseIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 21V9l9-5 9 5v12"></path><path d="M3 9h18M7 13h3v3H7zM14 13h3v3h-3zM8 21v-3h8v3"></path></svg>';
        }

        function chevronIcon(direction) {
            var path = direction === 'left' ? 'm15 18-6-6 6-6' : 'm9 18 6-6-6-6';
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="' + path + '"></path></svg>';
        }

        function renderWarehouseOptions() {
            $warehouseSelect.empty();
            warehouseStockState.warehouses.forEach(function (warehouse) {
                $('<option>')
                    .val(warehouse.warehouse_id)
                    .text(warehouse.warehouse_name)
                    .appendTo($warehouseSelect);
            });

            if (warehouseStockState.warehouseId != null) {
                $warehouseSelect.val(String(warehouseStockState.warehouseId));
            }
        }

        function renderWarehouseStockWidget() {
            renderWarehouseOptions();
            renderWarehouseStockRows();
        }

        function updatePageSize() {
            var listHeight = $list.innerHeight();
            var rowHeight = $list.find('.MPC-ws-row').first().outerHeight(true);
            if (!listHeight || !rowHeight) { return false; }

            var measuredPageSize = Math.max(1, Math.floor(listHeight / rowHeight));
            if (measuredPageSize === warehouseStockState.pageSize) { return false; }

            warehouseStockState.pageSize = measuredPageSize;
            warehouseStockState.page = 1;
            return true;
        }

        function renderWarehouseStockRows() {
            var rows = getSelectedWarehouseRows();
            var summary = getSummary(rows);
            var totalPages = Math.max(1, Math.ceil(rows.length / warehouseStockState.pageSize));
            if (warehouseStockState.page > totalPages) { warehouseStockState.page = totalPages; }

            var start = (warehouseStockState.page - 1) * warehouseStockState.pageSize;
            var pageRows = rows.slice(start, start + warehouseStockState.pageSize);

            $summary.html(
                '<span><strong>' + formatQty(summary.locators) + '</strong> ' + escapeHtml(label('VAS_Locators', 'locators')) + '</span>' +
                '<i></i>' +
                '<span><strong>' + formatQty(summary.quantity) + '</strong> ' + escapeHtml(label('VAS_Qty', 'qty')) + '</span>' +
                '<i></i>' +
                '<span><strong class="MPC-ws-accent" title="' + escapeHtml(formatAmount(summary.value)) + '">' + escapeHtml(formatAmount(summary.value)) + '</strong> ' + escapeHtml(label('VAS_Value', 'value')) + '</span>'
            );

            if (!pageRows.length) {
                $list.html('<div class="MPC-ws-empty">' + escapeHtml(label('VAS_NoWarehouseStock', 'No stock found for this warehouse.')) + '</div>');
            } else {
                var html = '';
                pageRows.forEach(function (row) {
                    var shares = getAgeShares(row);
                    var spark = renderStackedBar(shares, 'MPC-ws-spark-svg');
                    var quantity = formatQty(row.total_qty);
                    var amount = formatAmount(row.total_value);

                    html +=
                        '<button type="button" class="MPC-ws-row" data-locator-id="' + Number(row.locator_id) + '">' +
                            '<span class="MPC-ws-locator">' +
                                '<span class="MPC-ws-locator-line"><strong>' + escapeHtml(row.locator_code) + '</strong> <small>' + escapeHtml(row.locator_name) + '</small></span>' +
                                '<span class="MPC-ws-spark">' + spark + '</span>' +
                            '</span>' +
                            '<span class="MPC-ws-qty" title="' + escapeHtml(quantity) + '">' + escapeHtml(quantity) + '</span>' +
                            '<span class="MPC-ws-value" title="' + escapeHtml(amount) + '">' + escapeHtml(amount) + '</span>' +
                            '<span class="MPC-ws-chevron">' + chevronIcon('right') + '</span>' +
                        '</button>';
                });
                $list.html(html);

                if (updatePageSize()) {
                    renderWarehouseStockRows();
                    return;
                }
            }

            var first = rows.length ? start + 1 : 0;
            var last = rows.length ? start + pageRows.length : 0;
            $footer.html(
                '<span>' + escapeHtml(label('VAS_Showing', 'Showing')) + ' ' + first + '\u2013' + last + ' ' + escapeHtml(label('VAS_Of', 'of')) + ' ' + rows.length + '</span>' +
                '<span class="MPC-ws-pager">' +
                    '<button type="button" data-page="previous" aria-label="' + escapeHtml(label('VAS_PreviousPage', 'Previous page')) + '"' + (warehouseStockState.page === 1 ? ' disabled' : '') + '>' + chevronIcon('left') + '</button>' +
                    '<strong>' + warehouseStockState.page + ' ' + escapeHtml(label('VAS_Of', 'of')) + ' ' + totalPages + '</strong>' +
                    '<button type="button" data-page="next" aria-label="' + escapeHtml(label('VAS_NextPage', 'Next page')) + '"' + (warehouseStockState.page === totalPages ? ' disabled' : '') + '>' + chevronIcon('right') + '</button>' +
                '</span>'
            );
        }

        function openAgeModal(row) {
            var shares = getAgeShares(row);
            var totalValue = ageBands.reduce(function (sum, band) {
                return sum + Number(row[band.key] || 0);
            }, 0);

            var stack = renderStackedBar(shares, 'MPC-ws-age-stack-svg');

            var rows = ageBands.map(function (band, index) {
                var value = Number(row[band.key] || 0);
                var amount = formatAmount(value);
                return '<div class="MPC-ws-age-row">' +
                    '<span class="MPC-ws-age-label"><i class="' + band.className + '"></i>' + escapeHtml(label(band.messageKey, band.fallback)) + '</span>' +
                    '<span class="MPC-ws-age-track">' + renderBandBar(shares[index], band) + '</span>' +
                    '<span class="MPC-ws-age-value"><strong title="' + escapeHtml(amount) + '">' + escapeHtml(amount) + '</strong><small>' + Math.round(shares[index]) + '% ' + escapeHtml(label('VAS_OfValue', 'of value')) + '</small></span>' +
                '</div>';
            }).join('');

            $modalBadge.text(formatQty(row.total_qty) + ' ' + label('VAS_Units', 'units'));
            $modalBody.html(
                '<div class="MPC-ws-age-total"><strong title="' + escapeHtml(formatAmount(totalValue)) + '">' + escapeHtml(formatAmount(totalValue)) + '</strong><span>' + escapeHtml(label('VAS_TotalCarryingValueLocator', 'total carrying value in this locator')) + '</span></div>' +
                '<div class="MPC-ws-age-stack">' + stack + '</div>' +
                '<div class="MPC-ws-age-rows">' + rows + '</div>'
            );

            $modal.addClass('MPC-ws-modal-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-ws-body-lock');
            $modal.find('.MPC-ws-modal-close').trigger('focus');
        }

        function closeAgeModal() {
            if (!$modal) { return; }
            $modal.removeClass('MPC-ws-modal-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-ws-body-lock');
        }

        function showError() {
            $summary.empty();
            $list.html('<div class="MPC-ws-empty">' + escapeHtml(label('VAS_CouldntLoad', "Couldn't load")) + '</div>');
            $footer.empty();
        }

        function hasErrorProp(obj) {
            return obj !== null && typeof obj === 'object' && 'error' in obj;
        }

        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_080_WarehouseStockWidget/GetWarehouses',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var warehouses = parseResponse(response);
                    if (hasErrorProp(warehouses)) {
                        console.error('[VAS_080] GetWarehouses error:', warehouses.error, '|', warehouses.detail || '');
                        showError(); return;
                    }

                    warehouseStockState.warehouses = Array.isArray(warehouses) ? warehouses : [];
                    warehouseStockState.warehouseId = warehouseStockState.warehouses.length ? warehouseStockState.warehouses[0].warehouse_id : null;
                    renderWarehouseOptions();

                    $.ajax({
                        url: VIS.Application.contextUrl + 'VAS_080_WarehouseStockWidget/GetStockRows',
                        type: 'GET',
                        cache: false,
                        success: function (stockResponse) {
                            var stockResult = parseResponse(stockResponse);
                            if (hasErrorProp(stockResult)) {
                                console.error('[VAS_080] GetStockRows error:', stockResult.error, '|', stockResult.detail || '');
                                showError(); return;
                            }
                            warehouseStockState.rows = stockResult.rows || [];
                            warehouseStockState.currencySymbol = stockResult.currency_symbol || '';
                            warehouseStockState.currencyIso = stockResult.currency_iso || '';
                            warehouseStockState.stdPrecision = stockResult.std_precision;
                            renderWarehouseStockWidget();
                        },
                        error: function (xhr) {
                            console.error('[VAS_080] GetStockRows HTTP error:', xhr.status, xhr.responseText);
                            showError();
                        }
                    });
                },
                error: function (xhr) {
                    console.error('[VAS_080] GetWarehouses HTTP error:', xhr.status, xhr.responseText);
                    showError();
                }
            });
        }

        function createWidget() {
            $card = $(
                '<div class="MPC-ws-card">' +
                    '<div class="MPC-ws-header">' +
                        '<span class="MPC-ws-icon">' + warehouseIcon() + '</span>' +
                        '<span class="MPC-ws-title">' + escapeHtml(label('VAS_WarehouseStock', 'Warehouse Stock')) + '</span>' +
                        '<span class="MPC-ws-select-wrap"><select class="MPC-ws-select" aria-label="' + escapeHtml(label('Warehouse', 'Warehouse')) + '"></select></span>' +
                    '</div>' +
                    '<div class="MPC-ws-summary"></div>' +
                    '<div class="MPC-ws-table-head"><span>' + escapeHtml(label('VAS_Locator', 'Locator')) + '</span><span>' + escapeHtml(label('VAS_Qty', 'Qty')) + '</span><span>' + escapeHtml(label('VAS_Value', 'Value')) + '</span><span></span></div>' +
                    '<div class="MPC-ws-list"></div>' +
                    '<div class="MPC-ws-footer"></div>' +
                '</div>'
            );

            $warehouseSelect = $card.find('.MPC-ws-select');
            $summary = $card.find('.MPC-ws-summary');
            $list = $card.find('.MPC-ws-list');
            $footer = $card.find('.MPC-ws-footer');
            $root.append($card);

            $modal = $(
                '<div class="MPC-ws-modal" aria-hidden="true" role="dialog" aria-modal="true" aria-labelledby="MPC-ws-modal-title-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '">' +
                    '<div class="MPC-ws-modal-scrim"></div>' +
                    '<div class="MPC-ws-modal-dialog">' +
                        '<div class="MPC-ws-modal-header">' +
                            '<span id="MPC-ws-modal-title-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '">' + escapeHtml(label('VAS_InventoryValueByAge', 'Inventory Value by Age')) + '</span>' +
                            '<span class="MPC-ws-modal-badge"></span>' +
                            '<button type="button" class="MPC-ws-modal-close" aria-label="' + escapeHtml(label('Close', 'Close')) + '">\u00d7</button>' +
                        '</div>' +
                        '<div class="MPC-ws-modal-body"></div>' +
                    '</div>' +
                '</div>'
            );

            $modalBadge = $modal.find('.MPC-ws-modal-badge');
            $modalBody = $modal.find('.MPC-ws-modal-body');
            $('body').append($modal);

            $warehouseSelect.on('change', function () {
                warehouseStockState.warehouseId = Number($(this).val());
                warehouseStockState.page = 1;
                renderWarehouseStockRows();
            });

            $root.on('click', '[data-page]', function () {
                var direction = $(this).attr('data-page');
                warehouseStockState.page += direction === 'next' ? 1 : -1;
                renderWarehouseStockRows();
            });

            $root.on('click', '.MPC-ws-row', function () {
                var locatorId = Number($(this).attr('data-locator-id'));
                var row = warehouseStockState.rows.find(function (stockRow) {
                    return Number(stockRow.locator_id) === locatorId;
                });
                if (row) { openAgeModal(row); }
            });

            $modal.on('click', '.MPC-ws-modal-close, .MPC-ws-modal-scrim', closeAgeModal);
            $(document).on('keydown.MPCWarehouseStock-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget'), function (event) {
                if (event.key === 'Escape') { closeAgeModal(); }
            });
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        this.refreshWidget = function () {
            warehouseStockState.page = 1;
            loadData();
        };

        this.widgetSizeChange = function () {
            if ($list) { renderWarehouseStockRows(); }
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            closeAgeModal();
            $(document).off('keydown.MPCWarehouseStock-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget'));
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
        };
    };

    VAS.VAS_080_WarehouseStockWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_080_WarehouseStockWidget.prototype.widgetSizeChange = function () {
        this.widgetSizeChange();
    };

    VAS.VAS_080_WarehouseStockWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_080_WarehouseStockWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
