/*
 * Stock Movement - Today message summary
 * VAS_096_StockMovementToday       Stock Movement - Today
 * Item                             Item
 * Type                             Type
 * Qty                              Qty
 * Location                         Location
 * VAS_096_Location                 Location
 * VAS_096_Receipt                  Receipt
 * VAS_096_VendorReturn             Vendor Return
 * VAS_096_CustomerReturn           Customer Return
 * VAS_096_ShipmentIssue            Shipment / Issue
 * VAS_096_MovementIn               Movement In
 * VAS_096_MovementOut              Movement Out
 * VAS_096_InventoryIncrease        Inventory Increase
 * VAS_096_InventoryDecrease        Inventory Decrease
 * VAS_096_ProductionIn             Production In
 * VAS_096_ProductionOut            Production Out
 * VAS_096_ShowingLatestMovements   Showing latest 5 movements
 * VAS_096_MovementDetail           Stock Movement Detail
 * MovementType                     Movement Type
 * Quantity                         Quantity
 * Warehouse                        Warehouse
 * Locator                          Locator
 * MovementDate                     Movement Date
 * VAS_096_NoMovementsToday         No stock movements today.
 * Close                            Close
 * VAS_CouldntLoad                  Couldn't load
 */

; (function (VAS, $) {
    "use strict";

    /* Keep --dash-inline-size on :root equal to the dashboard container's pixel
       width so the widget's em sizing resolves against the dashboard content
       area (matching the reference's container query), not the viewport. One
       document-level observer serves every widget; without a marked container
       or ResizeObserver the CSS falls back to 100vw. */
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

    VAS.VAS_096_StockMovementTodayWidget = function () {
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
            return value && value.charAt(0) !== '[' ? value : fallback;
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

        // Review #11: the modal shows the movement date only - no time part.
        function formatDate(value) {
            if (!value) { return ''; }
            var date = new Date(value);
            if (isNaN(date.getTime())) { return value; }
            return date.toLocaleDateString(window.navigator.language, {
                year: 'numeric',
                month: 'short',
                day: 'numeric'
            });
        }

        function typeInfo(code) {
            // tone maps to the reference pill palette by movement class:
            // Receipt/Transfer -> info, outbound issues/returns -> bad,
            // builds/customer returns (inbound) -> ok, adjustments -> warn.
            var types = {
                'V+': ['VAS_096_Receipt', 'Receipt', 'info'],
                'V-': ['VAS_096_VendorReturn', 'Vendor Return', 'bad'],
                'C+': ['VAS_096_CustomerReturn', 'Customer Return', 'ok'],
                'C-': ['VAS_096_ShipmentIssue', 'Shipment / Issue', 'bad'],
                'M+': ['VAS_096_MovementIn', 'Movement In', 'info'],
                'M-': ['VAS_096_MovementOut', 'Movement Out', 'info'],
                'I+': ['VAS_096_InventoryIncrease', 'Inventory Increase', 'warn'],
                'I-': ['VAS_096_InventoryDecrease', 'Inventory Decrease', 'warn'],
                'P+': ['VAS_096_ProductionIn', 'Production In', 'ok'],
                'P-': ['VAS_096_ProductionOut', 'Production Out', 'ok']
            };
            var type = types[code];
            return type
                ? { text: label(type[0], type[1]), tone: type[2] }
                : { text: code || '', tone: 'neutral' };
        }

        function locationText(row) {
            var parts = [];
            if (row.warehouse_name) { parts.push(row.warehouse_name); }
            if (row.locator_value) { parts.push(row.locator_value); }
            return parts.join(' \u00b7 ');
        }

        function addDetail($container, detailLabel, value, isStrong) {
            var $detail = $('<div class="MPC-smt-detail"></div>');
            $detail.append($('<span class="MPC-smt-detail-label"></span>').text(detailLabel));
            $detail.append(
                $('<span class="MPC-smt-detail-value"></span>')
                    .toggleClass('is-strong', !!isStrong)
                    .text(value || '')
            );
            $container.append($detail);
        }

        function closeModal() {
            if (!$modal) { return; }
            $modal.removeClass('MPC-smt-modal-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-smt-body-lock');
        }

        function openModal(row) {
            if (!$modal || !row) { return; }

            var movementType = typeInfo(row.movement_type);
            $modal.find('.MPC-smt-modal-title').text(row.item_name || '');
            $modal.find('.MPC-smt-modal-badge')
                .attr('class', 'MPC-smt-modal-badge MPC-smt-type MPC-smt-type-' + movementType.tone)
                .text(movementType.text);
            var $details = $modal.find('.MPC-smt-modal-details').empty();
            addDetail($details, label('Item', 'Item'), row.item_name, true);
            addDetail($details, label('MovementType', 'Movement'), movementType.text);
            addDetail($details, label('Quantity', 'Quantity'), formatQuantity(row.movement_qty));
            addDetail($details, label('Locator', 'Location'), row.locator_value || '');
            addDetail($details, label('Date', 'Date'), formatDate(row.movement_date));
            addDetail($details, label('Warehouse', 'Warehouse'), row.warehouse_name || '');

            var $effect = $modal.find('.MPC-smt-stock-effect').empty().addClass('MPC-smt-hidden');
            if (row.qty_on_hand != null) {
                var prec = getPrecision();
                var fmtQ = function (n) {
                    return Number(n).toLocaleString(window.navigator.language, {
                        minimumFractionDigits: prec,
                        maximumFractionDigits: prec
                    });
                };
                var qtyAfter = Number(row.qty_on_hand || 0);
                var qtyChange = Number(row.movement_qty || 0);
                var qtyBefore = qtyAfter - qtyChange;
                $effect.append($('<div class="MPC-smt-effect-head"></div>').text(label('VAS_StockEffect', 'Stock Effect')));
                $effect.append(
                    '<table class="MPC-smt-effect-table">' +
                        '<thead><tr>' +
                            '<th>' + label('VAS_Before', 'Before') + '</th>' +
                            '<th class="is-center">' + label('VAS_Change', 'Change') + '</th>' +
                            '<th class="is-right">' + label('VAS_After', 'After') + '</th>' +
                        '</tr></thead>' +
                        '<tbody><tr>' +
                            '<td class="is-strong">' + fmtQ(qtyBefore) + '</td>' +
                            '<td class="is-center">' + formatQuantity(qtyChange) + '</td>' +
                            '<td class="is-right">' + fmtQ(qtyAfter) + '</td>' +
                        '</tr></tbody>' +
                    '</table>'
                );
                $effect.removeClass('MPC-smt-hidden');
            }

            $modal.addClass('MPC-smt-modal-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-smt-body-lock');
            $modal.find('.MPC-smt-modal-dialog').trigger('focus');
        }

        function renderRows(totalRecords) {
            $list.empty();
            if (!rows.length) {
                $list.addClass('MPC-smt-hidden');
                $footer.addClass('MPC-smt-hidden');
                $empty.removeClass('MPC-smt-hidden').text(label('VAS_096_NoMovementsToday', 'No stock movements today.'));
                return;
            }

            $empty.addClass('MPC-smt-hidden');
            $list.removeClass('MPC-smt-hidden');
            $footer.toggleClass('MPC-smt-hidden', totalRecords <= 5);
            $footer.text(label('VAS_096_ShowingLatestMovements', 'Showing latest 5 movements'));

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
                url: VIS.Application.contextUrl + 'VAS_096_StockMovementTodayWidget/GetStockMovements',
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
                    '<div class="MPC-smt-modal-dialog" role="dialog" aria-modal="true" tabindex="-1" aria-labelledby="' + modalTitleId + '">' +
                        '<div class="MPC-smt-modal-header">' +
                            '<span class="MPC-smt-modal-title" id="' + modalTitleId + '"></span>' +
                            '<span class="MPC-smt-modal-badge"></span>' +
                            '<button type="button" class="MPC-smt-modal-close">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                                    '<line x1="6" y1="6" x2="18" y2="18"/>' +
                                    '<line x1="18" y1="6" x2="6" y2="18"/>' +
                                '</svg>' +
                            '</button>' +
                        '</div>' +
                        '<div class="MPC-smt-modal-body">' +
                            '<div class="MPC-smt-modal-details"></div>' +
                            '<div class="MPC-smt-stock-effect MPC-smt-hidden"></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            var closeText = label('Close', 'Close');
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
                                '<polyline points="2 12 7 12 10 4 14 20 17 12 22 12"></polyline>' +
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

            $card.find('.MPC-smt-title').text(label('VAS_096_StockMovementToday', 'Stock Movement - Today'));
            $card.find('.MPC-smt-head-item').text(label('Item', 'Item'));
            $card.find('.MPC-smt-head-type').text(label('Type', 'Type'));
            $card.find('.MPC-smt-head-quantity').text(label('Qty', 'Qty'));
            $card.find('.MPC-smt-head-location').text(label('VAS_096_Location', 'Location'));
            $list = $card.find('.MPC-smt-list');
            $empty = $card.find('.MPC-smt-empty');
            $footer = $card.find('.MPC-smt-footer');
            $busy = $card.find('.vis-busyindicatorouterwrap');

            $root.on('click' + eventNamespace, '.MPC-smt-row', function () {
                var row = rows[Number($(this).attr('data-row-index'))];
                openModal(row);
            });

            $root.append($card);
            ensureDashInlineSizeVar($root);
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

    VAS.VAS_096_StockMovementTodayWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_096_StockMovementTodayWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_096_StockMovementTodayWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_096_StockMovementTodayWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
