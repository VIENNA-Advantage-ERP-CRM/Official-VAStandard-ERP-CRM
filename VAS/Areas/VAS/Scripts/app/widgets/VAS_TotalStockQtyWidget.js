/**
 * Total Stock Qty Widget
 * Summary Message Table
 *  # | Current Text                | Message Key
 * ---+-----------------------------+------------------------------
 *  1 | Total Stock Qty             | VAS_TotalStockQty
 *  2 | Units across all warehouses | VAS_UnitsAcrossAllWarehouses
 *  3 | Couldn't load               | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_TotalStockQtyWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-total-stock-root">');
        var $value;
        var $meta;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function formatCompact(value) {
            var number = Number(value || 0);
            var absolute = Math.abs(number);
            var divisor = 1;
            var suffix = '';

            if (absolute >= 10000000) {
                divisor = 10000000;
                suffix = 'Cr';
            } else if (absolute >= 1000000) {
                divisor = 100000;
                suffix = 'L';
            } else if (absolute >= 1000) {
                divisor = 1000;
                suffix = 'K';
            }

            if (!suffix) {
                return number.toLocaleString(window.navigator.language, {
                    maximumFractionDigits: 2
                });
            }

            return (number / divisor).toFixed(1).replace(/\.0$/, '') + suffix;
        }

        function loadTotal() {
            $value.text('\u2014');
            $meta.text(label('VAS_UnitsAcrossAllWarehouses', 'Units across all warehouses'));

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_TotalStockQtyWidget/GetTotalStockQty',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (result && !result.error) {
                        $value.text(formatCompact(result.total_stock_qty));
                        return;
                    }

                    showError();
                },
                error: showError
            });
        }

        function showError() {
            $value.text('\u2014');
            $meta.text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        this.Initalize = function () {
            var $card = $(
                '<div class="MPC-total-stock-card" aria-live="polite">' +
                    '<div class="MPC-total-stock-label"></div>' +
                    '<div class="MPC-total-stock-value">\u2014</div>' +
                    '<div class="MPC-total-stock-meta"></div>' +
                '</div>'
            );

            $card.find('.MPC-total-stock-label').text(label('VAS_TotalStockQty', 'Total Stock Qty'));
            $value = $card.find('.MPC-total-stock-value');
            $meta = $card.find('.MPC-total-stock-meta');
            $root.append($card);
            loadTotal();
        };

        this.refreshWidget = function () {
            loadTotal();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_TotalStockQtyWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_TotalStockQtyWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_TotalStockQtyWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_TotalStockQtyWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
