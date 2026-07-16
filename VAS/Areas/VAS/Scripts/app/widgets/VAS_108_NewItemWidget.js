/**
 * New Item Widget (Quick Action)
 * 1x1 dashed quick-action tile. Clicking it opens the widget's configured
 * item-master window on a NEW record, via the widget framework's value-changed
 * channel (IsTabInNewMode) - the same open-in-new-mode logic as the New GRN
 * widget. No backend/controller: this tile only fires the open action.
 * Widget number 108 - reassign on hand-off.
 *
 * Summary Message Table
 *  # | Current Text        | Message Key
 * ---+---------------------+--------------------------------
 *  1 | New Item            | VAS_108_NewItem
 *  2 | Add to item master  | VAS_108_AddToItemMaster
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

    VAS.VAS_108_NewItemWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-niqa-root">');

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

        // Open the widget's configured window (the Item / Product window) directly
        // on a NEW record through the widget framework's value-changed channel.
        // The host reuses the same window and starts a blank record.
        function openNewItem() {
            var windowParam = {
                "IsTabInNewMode": "true",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        function createWidget() {
            var title = lbl('VAS_108_NewItem', 'New Item');
            var $card = $(
                '<button type="button" class="vas-niqa-card" aria-label="' + escapeHtml(title) + '">' +
                    '<span class="vas-niqa-iconrow">' +
                        '<span class="vas-niqa-ico">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                        '</span>' +
                    '</span>' +
                    '<span class="vas-niqa-text">' +
                        '<span class="vas-niqa-title">' + escapeHtml(title) + '</span>' +
                        '<span class="vas-niqa-sub">' + escapeHtml(lbl('VAS_108_AddToItemMaster', 'Add to item master')) + '</span>' +
                    '</span>' +
                '</button>'
            );

            $card.on('click', function () { openNewItem(); });
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

    VAS.VAS_108_NewItemWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_108_NewItemWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_108_NewItemWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_108_NewItemWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_108_NewItemWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_108_NewItemWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
