/**
 * Active Items Widget (KPI Card)
 * Widget number 109 - reassign on hand-off.
 * Shows the count of active, non-discontinued items; meta shows the
 * discontinued count.
 * Backend - VAS_109_ActiveItemsWidget/GetActiveItems
 * Summary Message Table
 *  # | Current Text     | Message Key
 * ---+------------------+------------------------------
 *  1 | Active Items     | VAS_109_ActiveItems
 *  2 | discontinued     | VAS_109_Discontinued
 *  3 | Couldn't load    | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_109_ActiveItemsWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-active-items-root">');
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

        function loadActiveItems() {
            if (request && request.readyState !== 4) { request.abort(); }
            $value.text('—');

            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_109_ActiveItemsWidget/GetActiveItems',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (result && !result.error) {
                        $value.text(formatCount(result.active_count));
                        $meta.text(formatCount(result.discontinued_count) + ' ' + label('VAS_109_Discontinued', 'discontinued'));
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
            $value.text('—');
            $meta.text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        this.Initalize = function () {
            var $card = $(
                '<div class="MPC-active-items-card" aria-live="polite">' +
                    '<div class="MPC-active-items-label"></div>' +
                    '<div class="MPC-active-items-value">—</div>' +
                    '<div class="MPC-active-items-meta"></div>' +
                '</div>'
            );

            $card.find('.MPC-active-items-label').text(label('VAS_109_ActiveItems', 'Active Items'));
            $value = $card.find('.MPC-active-items-value');
            $meta = $card.find('.MPC-active-items-meta');
            $root.append($card);
            loadActiveItems();
        };

        this.refreshWidget = function () {
            loadActiveItems();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (request && request.readyState !== 4) { request.abort(); }
            $root.remove();
        };
    };

    VAS.VAS_109_ActiveItemsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_109_ActiveItemsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_109_ActiveItemsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_109_ActiveItemsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
