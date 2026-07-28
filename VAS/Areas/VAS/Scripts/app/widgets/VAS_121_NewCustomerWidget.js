/**
 * VAS_121 New Customer Widget (Quick Action, Customers module dashboard)
 * Purpose - 1x1 dashed quick-action tile. Clicking it opens the host Customer
 *           (C_BPartner) window on a NEW record via the widget framework's
 *           value-changed channel (IsTabInNewMode) - the same open-in-new-mode
 *           path as VAS_108 New Item / VAS_082 New GRN. New customers are created
 *           through the real window (which persists via the M/X business-partner
 *           classes), never a hand-built insert form (Prompt_Instructions:
 *           "No Direct INSERT Queries"). No backend/controller: this tile only
 *           fires the open action.
 * Design  - new-customer.html (attached) + Design Specs/dashboard-widgets.md
 *           "Quick Action Widget": pale-blue glass, 2px dashed #9ED1FF border,
 *           centred blue "+" well above a bold title and muted subtitle. Internal
 *           sizing in em against the widget-root clamp; borders/radii in px.
 *           CSS namespaced vas121-* (Prompt_Instructions MPC prefix rule).
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  # | Current Text     | Message Key
 * ---+------------------+--------------------------------
 *  1 | New Customer     | VAS_121_NewCustomer
 *  2 | Add a customer   | VAS_121_AddCustomer
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget. */
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

    VAS.VAS_121_NewCustomerWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas121-root">');

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

        // Open the widget's configured window (the Customer / Business Partner
        // window) directly on a NEW record through the widget framework's
        // value-changed channel. The host reuses the same window and starts a
        // blank record (IsTabInNewMode) - no duplicate window is opened.
        function openNewCustomer() {
            var windowParam = {
                "IsTabInNewMode": "true",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        function createWidget() {
            var title = lbl('VAS_121_NewCustomer', 'New Customer');
            var $card = $(
                '<button type="button" class="vas121-card" aria-label="' + escapeHtml(title) + '">' +
                    '<span class="vas121-well">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                    '</span>' +
                    '<span class="vas121-text">' +
                        '<span class="vas121-title">' + escapeHtml(title) + '</span>' +
                        '<span class="vas121-sub">' + escapeHtml(lbl('VAS_121_AddCustomer', 'Add a customer')) + '</span>' +
                    '</span>' +
                '</button>'
            );

            $card.on('click', function () { openNewCustomer(); });
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

    /* Relay the fired value (open-in-new-mode params) to the registered host. */
    VAS.VAS_121_NewCustomerWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    /* The widget host registers itself here so the widget can drive the host. */
    VAS.VAS_121_NewCustomerWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_121_NewCustomerWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_121_NewCustomerWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_121_NewCustomerWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_121_NewCustomerWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
