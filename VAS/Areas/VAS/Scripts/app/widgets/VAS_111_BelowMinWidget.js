/**
 * Below Min Widget (KPI Card)
 * Widget number 111 - reassign on hand-off.
 * Shows the count of items whose on-hand stock is at or below the reorder
 * point (per-warehouse minimum from M_Replenish). Severity: warn.
 * Backend - VAS_111_BelowMinWidget/GetBelowMin
 * Summary Message Table
 *  # | Current Text     | Message Key
 * ---+------------------+------------------------------
 *  1 | Below Min        | VAS_111_BelowMin
 *  2 | Need attention   | VAS_111_NeedAttention
 *  3 | Couldn't load    | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_111_BelowMinWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-below-min-root">');
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

        function loadBelowMin() {
            if (request && request.readyState !== 4) { request.abort(); }
            $value.text('—');

            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_111_BelowMinWidget/GetBelowMin',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (result && !result.error) {
                        var count = Number(result.below_min_count || 0);
                        $value.text(formatCount(count));
                        // Warn tone only when items actually need attention.
                        $value.toggleClass('MPC-below-min-warn', count > 0);
                        $meta.text(label('VAS_111_NeedAttention', 'Need attention'));
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
            $value.text('—').removeClass('MPC-below-min-warn');
            $meta.text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        this.Initalize = function () {
            var $card = $(
                '<div class="MPC-below-min-card" aria-live="polite">' +
                    '<div class="MPC-below-min-label"></div>' +
                    '<div class="MPC-below-min-value">—</div>' +
                    '<div class="MPC-below-min-meta"></div>' +
                '</div>'
            );

            $card.find('.MPC-below-min-label').text(label('VAS_111_BelowMin', 'Below Min'));
            $value = $card.find('.MPC-below-min-value');
            $meta = $card.find('.MPC-below-min-meta');
            $root.append($card);
            loadBelowMin();
        };

        this.refreshWidget = function () {
            loadBelowMin();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (request && request.readyState !== 4) { request.abort(); }
            $root.remove();
        };
    };

    VAS.VAS_111_BelowMinWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_111_BelowMinWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_111_BelowMinWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_111_BelowMinWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
