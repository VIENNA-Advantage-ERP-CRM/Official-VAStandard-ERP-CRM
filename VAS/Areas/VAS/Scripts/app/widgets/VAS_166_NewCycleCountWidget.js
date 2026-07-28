/**
 * New Cycle Count Widget (Quick Action)
 * 1x1 quick-action tile launching the Physical Inventory window on a NEW record
 * via the widget framework's value-changed channel (IsTabInNewMode).
 * 100% Copy-paste architecture and code pattern from VAS_108_NewItemWidget.js.
 * Prefix - VAS_166_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | New Cycle Count                                  | VAS_166_NewCycleCount
 *  2  | Schedule a count session                         | VAS_166_ScheduleCountSession
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

    VAS.VAS_166_NewCycleCountWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-new-cycle-count-root">');

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
        function openNewCycleCount() {
            var windowParam = {
                "IsTabInNewMode": "true",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        function createWidget() {
            var title = lbl('VAS_166_NewCycleCount', 'New Cycle Count');
            var $card = $(
                '<button type="button" class="vas-new-cycle-count-card" aria-label="' + escapeHtml(title) + '">' +
                    '<span class="vas-new-cycle-count-iconrow">' +
                        '<span class="vas-new-cycle-count-ico">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                        '</span>' +
                    '</span>' +
                    '<span class="vas-new-cycle-count-text">' +
                        '<span class="vas-new-cycle-count-title">' + escapeHtml(title) + '</span>' +
                        '<span class="vas-new-cycle-count-sub">' + escapeHtml(lbl('VAS_166_ScheduleCountSession', 'Schedule a count session')) + '</span>' +
                    '</span>' +
                '</button>'
            );

            $card.on('click', function () { openNewCycleCount(); });
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

    VAS.VAS_166_NewCycleCountWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_166_NewCycleCountWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_166_NewCycleCountWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_166_NewCycleCountWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_166_NewCycleCountWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_166_NewCycleCountWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
