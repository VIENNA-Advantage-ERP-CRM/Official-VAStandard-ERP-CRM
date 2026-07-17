/**
 * Slow Movers Widget (KPI Card)
 * Widget number 112 - reassign on hand-off.
 * Shows the count of items laying in the warehouse (on-hand > 0) with no
 * sale/issue in the last 30 days (excludes inactive/discontinued). Severity: danger.
 * Backend - VAS_112_SlowMoversWidget/GetSlowMovers
 * Summary Message Table
 *  # | Current Text     | Message Key
 * ---+------------------+------------------------------
 *  1 | Slow Movers      | VAS_112_SlowMovers
 *  2 | No sale in {0}d   | VAS_112_NoSaleInDays
 *  3 | Couldn't load    | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_112_SlowMoversWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-slow-movers-root">');
        var $value;
        var $meta;
        var request;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 0 });
        }

        function loadSlowMovers() {
            if (request && request.readyState !== 4) { request.abort(); }
            $value.text('—');

            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_112_SlowMoversWidget/GetSlowMovers',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (result && !result.error) {
                        var count = Number(result.slow_movers_count || 0);
                        var windowDays = Number(result.window_days) || 30;
                        $value.text(formatCount(count));
                        // Danger tone only when there are slow movers.
                        $value.toggleClass('MPC-slow-movers-danger', count > 0);
                        $meta.text(label('VAS_112_NoSaleInDays', 'No sale in {0}d').replace('{0}', windowDays));
                        return;
                    }

                    showError();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                }
            });
        }

        function showError() {
            $value.text('—').removeClass('MPC-slow-movers-danger');
            $meta.text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        this.Initalize = function () {
            var $card = $(
                '<div class="MPC-slow-movers-card" aria-live="polite">' +
                    '<div class="MPC-slow-movers-label"></div>' +
                    '<div class="MPC-slow-movers-value">—</div>' +
                    '<div class="MPC-slow-movers-meta"></div>' +
                '</div>'
            );

            $card.find('.MPC-slow-movers-label').text(label('VAS_112_SlowMovers', 'Slow Movers'));
            $value = $card.find('.MPC-slow-movers-value');
            $meta = $card.find('.MPC-slow-movers-meta');
            $root.append($card);
            loadSlowMovers();
        };

        this.refreshWidget = function () {
            loadSlowMovers();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (request && request.readyState !== 4) { request.abort(); }
            $root.remove();
        };
    };

    VAS.VAS_112_SlowMoversWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_112_SlowMoversWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_112_SlowMoversWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_112_SlowMoversWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
