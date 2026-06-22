/**
 * Low Stock Count Widget
 * Summary Message Table
 *  # | Current Text        | Message Key
 * ---+---------------------+--------------------------
 *  1 | Low Stock Count     | VAS_LowStockCount
 *  2 | Below reorder level | VAS_BelowReorderLevel
 *  3 | Couldn't load       | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_LowStockCountWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-low-stock-root">');
        var $value;
        var $meta;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function loadCount() {
            $value.text('\u2014');
            $meta.text(label('VAS_BelowReorderLevel', 'Below reorder level'));

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_LowStockCountWidget/GetLowStockCount',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (result && !result.error) {
                        var count = Number(result.low_stock_count || 0);
                        $value.text(count.toLocaleString(window.navigator.language));
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
                '<div class="MPC-low-stock-card" aria-live="polite">' +
                    '<div class="MPC-low-stock-label"></div>' +
                    '<div class="MPC-low-stock-value">\u2014</div>' +
                    '<div class="MPC-low-stock-meta"></div>' +
                '</div>'
            );

            $card.find('.MPC-low-stock-label').text(label('VAS_LowStockCount', 'Low Stock Count'));
            $value = $card.find('.MPC-low-stock-value');
            $meta = $card.find('.MPC-low-stock-meta');
            $root.append($card);
            loadCount();
        };

        this.refreshWidget = function () {
            loadCount();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_LowStockCountWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_LowStockCountWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_LowStockCountWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_LowStockCountWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
