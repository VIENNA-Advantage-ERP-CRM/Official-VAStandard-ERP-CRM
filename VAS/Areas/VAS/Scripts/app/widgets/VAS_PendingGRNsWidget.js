/**
 * Pending GRNs Widget
 * Summary Message Table
 *  # | Current Text  | Message Key
 * ---+---------------+-----------------
 *  1 | Pending GRNs  | VAS_PendingGRNs
 *  2 | Awaiting GRN  | VAS_AwaitingGRN
 *  3 | Couldn't load | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_PendingGRNsWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-pending-grns-root">');
        var $value;
        var $meta;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function loadCount() {
            $value.text('\u2014');
            $meta.text(label('VAS_AwaitingGRN', 'Awaiting GRN'));

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_PendingGRNsWidget/GetPendingGRNCount',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (result && !result.error) {
                        var count = Number(result.pending_grn_count || 0);
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
                '<div class="MPC-pending-grns-card" aria-live="polite">' +
                    '<div class="MPC-pending-grns-label"></div>' +
                    '<div class="MPC-pending-grns-value">\u2014</div>' +
                    '<div class="MPC-pending-grns-meta"></div>' +
                '</div>'
            );

            $card.find('.MPC-pending-grns-label').text(label('VAS_PendingGRNs', 'Pending GRNs'));
            $value = $card.find('.MPC-pending-grns-value');
            $meta = $card.find('.MPC-pending-grns-meta');
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

    VAS.VAS_PendingGRNsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_PendingGRNsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_PendingGRNsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_PendingGRNsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
