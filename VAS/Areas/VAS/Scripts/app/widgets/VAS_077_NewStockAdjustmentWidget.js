/**
 * Overall Inventory - New Stock Adjustment launcher (1x1).
 * Opens the existing Inventory Count / Physical Inventory window.
 *
 * Summary Message Table
 *  # | Current Text             | Message Key
 * ---+--------------------------+--------------------------
 *  1 | New Stock Adjustment     | VAS_NewStockAdjustment
 *  2 | Open Inventory Count     | VAS_OpenInventoryCount
 *  3 | Unable to open the window| VAS_CouldntOpenWindow
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_077_NewStockAdjustmentWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-stock-adjustment-root">');
        var $tile;
        var isOpening = false;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function openInventoryCount() {
            if (isOpening) { return; }
            isOpening = true;
            $tile.prop('disabled', true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_077_NewStockAdjustmentWidget/GetInventoryCountWindowId',
                type: 'GET',
                dataType: 'json',
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result.length) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result.length) { result = JSON.parse(result); }

                    var windowId = Number(result && result.window_id);
                    if (windowId > 0) {
                        /* Open the window in the SAME view directly on a NEW
                           record: an all-excluding query keeps existing
                           adjustments out and cmd_new switches the panel to
                           record entry (same pattern as the New GRN widget). */
                        var emptyQuery = new VIS.Query();
                        try {
                            emptyQuery.addRestriction("M_Inventory_ID", VIS.Query.prototype.EQUAL, 0);
                        } catch (e) { /* keep the plain query */ }

                        var view = VIS.viewManager.startWindow(windowId, emptyQuery);
                        if (view) {
                            view.onLoad = function () {
                                try {
                                    if (view.cPanel && view.cPanel.cmd_new) { view.cPanel.cmd_new(false); }
                                } catch (e) { /* window still opens; user can press New */ }
                            };
                        }
                        return;
                    }

                    VIS.ADialog.error('VAS_CouldntOpenWindow', true, '', label('VAS_CouldntOpenWindow', 'Unable to open the window.'));
                },
                error: function () {
                    VIS.ADialog.error('VAS_CouldntOpenWindow', true, '', label('VAS_CouldntOpenWindow', 'Unable to open the window.'));
                },
                complete: function () {
                    isOpening = false;
                    if ($tile) { $tile.prop('disabled', false); }
                }
            });
        }

        // Open the widget's configured window (Inventory Count / Physical
        // Inventory) directly on a NEW record through the widget framework's
        // value-changed channel. The host reuses the same window and starts a
        // blank record (IsTabInNewMode) - same approach as the New GRN widget.
        function openStockAdjustmentNewRecord() {
            var windowParam = {
                "IsTabInNewMode": "true",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        function createWidget() {
            $tile = $(
                '<button type="button" class="MPC-stock-adjustment-tile" role="button" tabindex="0">' +
                    '<span class="MPC-stock-adjustment-icon" aria-hidden="true">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                            '<path d="M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2"></path>' +
                            '<rect x="9" y="3" width="6" height="4" rx="1"></rect>' +
                            '<path d="M12 11v6M9 14h6"></path>' +
                        '</svg>' +
                    '</span>' +
                    '<span class="MPC-stock-adjustment-copy">' +
                        '<span class="MPC-stock-adjustment-title"></span>' +
                        '<span class="MPC-stock-adjustment-subtitle"></span>' +
                    '</span>' +
                '</button>'
            );

            var title = label('VAS_NewStockAdjustment', 'New Stock Adjustment');
            var subtitle = label('VAS_OpenInventoryCount', 'Open Inventory Count');

            $tile.attr('aria-label', title + '. ' + subtitle);
            $tile.find('.MPC-stock-adjustment-title').text(title);
            $tile.find('.MPC-stock-adjustment-subtitle').text(subtitle);
            // Open the window on a NEW record via the widget framework's
            // value-changed channel (IsTabInNewMode), reusing the same window
            // instead of opening a duplicate each click.
            $tile.on('click', openStockAdjustmentNewRecord);
            $root.append($tile);
        }

        this.Initalize = function () {
            createWidget();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if ($tile) { $tile.off('click', openStockAdjustmentNewRecord); }
            $root.remove();
        };
    };

    VAS.VAS_077_NewStockAdjustmentWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_077_NewStockAdjustmentWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_077_NewStockAdjustmentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_077_NewStockAdjustmentWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_077_NewStockAdjustmentWidget.prototype.refreshWidget = function () { };

    VAS.VAS_077_NewStockAdjustmentWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
