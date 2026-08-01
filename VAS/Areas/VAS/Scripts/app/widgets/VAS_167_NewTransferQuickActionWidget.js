/**
 * New Transfer Quick Action Widget
 * 1x1 quick-action tile launching the Material Transfer window on a NEW record
 * via the widget framework's value-changed channel (IsTabInNewMode).
 * ID Prefix: VAS_167_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | New Transfer                                     | VAS_167_NewTransfer
 *  2  | Move stock between sites                         | VAS_167_MoveStockBetweenSites
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    VAS.VAS_167_NewTransferQuickActionWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-new-transfer-quick-action-root">');

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        // Open the widget's configured window directly on a NEW record
        // through the widget framework's value-changed channel.
        function openNewTransfer() {
            var windowParam = {
                "IsTabInNewMode": "true",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        function createWidget() {
            var title = lbl('VAS_167_NewTransfer', 'New Transfer');
            var subtitle = lbl('VAS_167_MoveStockBetweenSites', 'Move stock between sites');

            var $card = $(
                '<button type="button" class="vas-new-transfer-quick-action-card" aria-label="' + escapeHtml(title) + '">' +
                    '<span class="vas-new-transfer-quick-action-iconrow">' +
                        '<span class="vas-new-transfer-quick-action-ico">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                        '</span>' +
                    '</span>' +
                    '<span class="vas-new-transfer-quick-action-text">' +
                        '<span class="vas-new-transfer-quick-action-title">' + escapeHtml(title) + '</span>' +
                        '<span class="vas-new-transfer-quick-action-sub">' + escapeHtml(subtitle) + '</span>' +
                    '</span>' +
                '</button>'
            );

            $card.on('click', function () { openNewTransfer(); });
            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
        };

        this.refreshWidget = function () { };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $root.off();
            $root.remove();
        };
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
