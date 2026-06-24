/*
 * Stock Movement - Today message summary
 * VAS_093_StockMovementToday       Stock Movement - Today
 * Item                             Item
 * Type                             Type
 * Qty                              Qty
 * Location                         Location
 * VAS_093_Receipt                  Receipt
 * VAS_093_VendorReturn             Vendor Return
 * VAS_093_CustomerReturn           Customer Return
 * VAS_093_ShipmentIssue            Shipment / Issue
 * VAS_093_MovementIn               Movement In
 * VAS_093_MovementOut              Movement Out
 * VAS_093_InventoryIncrease        Inventory Increase
 * VAS_093_InventoryDecrease        Inventory Decrease
 * VAS_093_ProductionIn             Production In
 * VAS_093_ProductionOut            Production Out
 * VAS_093_ShowingLatestMovements   Showing latest 5 movements
 * VAS_093_MovementDetail           Stock Movement Detail
 * MovementType                     Movement Type
 * Quantity                         Quantity
 * Warehouse                        Warehouse
 * Locator                          Locator
 * MovementDate                     Movement Date
 * VAS_093_NoMovementsToday         No stock movements today.
 * Close                            Close
 * VAS_CouldntLoad                  Couldn't load
 */

; (function (VAS, $) {
    "use strict";

    VAS.VAS_093_StockMovementTodayWidget = function () {
        var $self = this;
        var $root = $('<div class="MPC-stock-movement-root"></div>');
        var $list;
        var $empty;
        var $footer;
        var $busy;
        var $modal;
        var request = null;
        var rows = [];
        var eventNamespace = '.MPCStockMovementToday';

        function label(key, fallback) {
            var value = VIS.Msg.getMsg(key);
            return value && value !== key ? value : fallback;
        }

        function getPrecision() {
            try {
                return Number(VIS.Env.getCtx().getStdPrecision()) || 0;
            } catch (ignore) {
                return 2;
            }
        }

        function formatQuantity(value) {
            var quantity = Number(value || 0);
            var text = Math.abs(quantity).toLocaleString(window.navigator.language, {
                minimumFractionDigits: getPrecision(),
                maximumFractionDigits: getPrecision()
            });
            if (quantity > 0) { return '+' + text; }
            if (quantity < 0) { return '-' + text; }
            return text;
        }

        function formatDate(value) {
            if (!value) { return ''; }
            var date = new Date(value);
            if (isNaN(date.getTime())) { return value; }
            return date.toLocaleString(window.navigator.language, {
                year: 'numeric',
                month: 'short',
                day: 'numeric',
                hour: '2-digit',
                minute: '2-digit'
            });
        }

        function typeInfo(code) {
            var types = {
                'V+': ['VAS_093_Receipt', 'Receipt', 'in'],
                'V-': ['VAS_093_VendorReturn', 'Vendor Return', 'out'],
                'C+': ['VAS_093_CustomerReturn', 'Customer Return', 'in'],
                'C-': ['VAS_093_ShipmentIssue', 'Shipment / Issue', 'out'],
                'M+': ['VAS_093_MovementIn', 'Movement In', 'move'],
                'M-': ['VAS_093_MovementOut', 'Movement Out', 'move'],
                'I+': ['VAS_093_InventoryIncrease', 'Inventory Increase', 'in'],
                'I-': ['VAS_093_InventoryDecrease', 'Inventory Decrease', 'out'],
                'P+': ['VAS_093_ProductionIn', 'Production In', 'in'],
                'P-': ['VAS_093_ProductionOut', 'Production Out', 'out']
            };
            var type = types[code];
            return type
                ? { text: label(type[0], type[1]), tone: type[2] }
                : { text: code || '', tone: 'move' };
        }

        function locationText(row) {
            var parts = [];
            if (row.warehouse_name) { parts.push(row.warehouse_name); }
            if (row.locator_value) { parts.push(row.locator_value); }
            return parts.join(' \u00b7 ');
        }

        function addDetail($container, detailLabel, value) {
            var $detail = $('<div class="MPC-smt-detail"></div>');
            $detail.append($('<span class="MPC-smt-detail-label"></span>').text(detailLabel));
            $detail.append($('<span class="MPC-smt-detail-value"></span>').text(value || ''));
            $container.append($detail);
        }

        function closeModal() {
            if (!$modal) { return; }
            $modal.removeClass('MPC-smt-modal-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-smt-body-lock');
        }

        function openModal(row) {
            if (!$modal || !row) { return; }

            var movementType = typeInfo(row.movement_type).text;
            var $details = $modal.find('.MPC-smt-modal-details').empty();
            addDetail($details, label('Item', 'Item'), row.item_name);
            addDetail($details, label('MovementType', 'Movement Type'), movementType);
            addDetail($details, label('Quantity', 'Quantity'), formatQuantity(row.movement_qty));
            addDetail($details, label('Warehouse', 'Warehouse'), row.warehouse_name);
            addDetail($details, label('Locator', 'Locator'), row.locator_value);
            addDetail($details, label('MovementDate', 'Movement Date'), formatDate(row.movement_date));

            $modal.addClass('MPC-smt-modal-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-smt-body-lock');
            $modal.find('.MPC-smt-modal-close').trigger('focus');
        }

        function renderRows(totalRecords) {
            $list.empty();
            if (!rows.length) {
                $list.addClass('MPC-smt-hidden');
                $footer.addClass('MPC-smt-hidden');
                $empty.removeClass('MPC-smt-hidden').text(label('VAS_093_NoMovementsToday', 'No stock movements today.'));
                return;
            }

            $empty.addClass('MPC-smt-hidden');
            $list.removeClass('MPC-smt-hidden');
            $footer.toggleClass('MPC-smt-hidden', totalRecords <= 5);
            $footer.text(label('VAS_093_ShowingLatestMovements', 'Showing latest 5 movements'));

            rows.forEach(function (row, index) {
                var movementType = typeInfo(row.movement_type);
                var quantity = Number(row.movement_qty || 0);
                var $row = $('<button type="button" class="MPC-smt-row MPC-smt-grid"></button>')
                    .attr('data-row-index', index);
                var $item = $('<span class="MPC-smt-cell MPC-smt-item"></span>')
                    .text(row.item_name || '')
                    .attr('title', row.item_name || '');
                var $type = $('<span class="MPC-smt-cell MPC-smt-type"></span>')
                    .addClass('MPC-smt-type-' + movementType.tone)
                    .text(movementType.text)
                    .attr('title', movementType.text);
                var $quantity = $('<span class="MPC-smt-cell MPC-smt-quantity"></span>')
                    .toggleClass('MPC-smt-quantity-positive', quantity > 0)
                    .toggleClass('MPC-smt-quantity-negative', quantity < 0)
                    .text(formatQuantity(quantity));
                var location = locationText(row);
                var $location = $('<span class="MPC-smt-cell MPC-smt-location"></span>')
                    .text(location)
                    .attr('title', location);

                $row.append($item, $type, $quantity, $location);
                $list.append($row);
            });
        }

        function showError() {
            rows = [];
            $list.addClass('MPC-smt-hidden').empty();
            $footer.addClass('MPC-smt-hidden');
            $empty.removeClass('MPC-smt-hidden').text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        function setBusy(visible) {
            if ($busy) { $busy.toggleClass('MPC-smt-busy-hidden', !visible); }
        }

        function loadMovements() {
            if (request && request.readyState !== 4) { request.abort(); }

            setBusy(true);
            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_093_StockMovementTodayWidget/GetStockMovements',
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

                    rows = result.rows || [];
                    renderRows(Number(result.total_records || rows.length));
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                },
                complete: function () {
                    setBusy(false);
                }
            });
        }

        function createModal() {
            var modalTitleId = 'MPC-smt-modal-title-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $modal = $(
                '<div class="MPC-smt-modal" aria-hidden="true">' +
                    '<div class="MPC-smt-modal-scrim"></div>' +
                    '<div class="MPC-smt-modal-dialog" role="dialog" aria-modal="true" aria-labelledby="' + modalTitleId + '">' +
                        '<div class="MPC-smt-modal-header">' +
                            '<span class="MPC-smt-modal-title" id="' + modalTitleId + '"></span>' +
                            '<button type="button" class="MPC-smt-modal-close">\u00d7</button>' +
                        '</div>' +
                        '<div class="MPC-smt-modal-details"></div>' +
                    '</div>' +
                '</div>'
            );

            var closeText = label('Close', 'Close');
            $modal.find('.MPC-smt-modal-title').text(label('VAS_093_MovementDetail', 'Stock Movement Detail'));
            $modal.find('.MPC-smt-modal-close').attr({ 'aria-label': closeText, title: closeText });
            $('body').append($modal);

            $modal.on('click' + eventNamespace, '.MPC-smt-modal-close, .MPC-smt-modal-scrim', closeModal);
            $(document).on('keydown' + eventNamespace, function (event) {
                if (event.key === 'Escape') { closeModal(); }
            });
        }

        this.Initalize = function () {
            eventNamespace += '-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            var $card = $(
                '<div class="MPC-smt-card" aria-live="polite">' +
                    '<div class="MPC-smt-header">' +
                        '<span class="MPC-smt-icon" aria-hidden="true">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                '<path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4z"></path>' +
                                '<polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>' +
                                '<line x1="12" y1="22.08" x2="12" y2="12"></line>' +
                            '</svg>' +
                        '</span>' +
                        '<span class="MPC-smt-title"></span>' +
                    '</div>' +
                    '<div class="MPC-smt-table-head MPC-smt-grid">' +
                        '<span class="MPC-smt-head-item"></span>' +
                        '<span class="MPC-smt-head-type"></span>' +
                        '<span class="MPC-smt-head-quantity"></span>' +
                        '<span class="MPC-smt-head-location"></span>' +
                    '</div>' +
                    '<div class="MPC-smt-list"></div>' +
                    '<div class="MPC-smt-empty MPC-smt-hidden"></div>' +
                    '<div class="MPC-smt-footer MPC-smt-hidden"></div>' +
                    '<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>'
            );

            $card.find('.MPC-smt-title').text(label('VAS_093_StockMovementToday', 'Stock Movement - Today'));
            $card.find('.MPC-smt-head-item').text(label('Item', 'Item'));
            $card.find('.MPC-smt-head-type').text(label('Type', 'Type'));
            $card.find('.MPC-smt-head-quantity').text(label('Qty', 'Qty'));
            $card.find('.MPC-smt-head-location').text(label('Location', 'Location'));
            $list = $card.find('.MPC-smt-list');
            $empty = $card.find('.MPC-smt-empty');
            $footer = $card.find('.MPC-smt-footer');
            $busy = $card.find('.vis-busyindicatorouterwrap');

            $root.on('click' + eventNamespace, '.MPC-smt-row', function () {
                var row = rows[Number($(this).attr('data-row-index'))];
                openModal(row);
            });

            $root.append($card);
            createModal();
            loadMovements();
        };

        this.refreshWidget = function () {
            closeModal();
            loadMovements();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (request && request.readyState !== 4) { request.abort(); }
            closeModal();
            $root.off(eventNamespace);
            $(document).off(eventNamespace);
            if ($modal) {
                $modal.off(eventNamespace).remove();
                $modal = null;
            }
            $root.remove();
            rows = [];
        };
    };

    VAS.VAS_093_StockMovementTodayWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_093_StockMovementTodayWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_093_StockMovementTodayWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_093_StockMovementTodayWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
