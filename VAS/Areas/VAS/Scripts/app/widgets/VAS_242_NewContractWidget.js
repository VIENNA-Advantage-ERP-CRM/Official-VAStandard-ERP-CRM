/**
 * VAS_242 New Contract Widget (Quick Action, Service Contracts module dashboard)
 * Purpose - 1x1 quick-action tile. Clicking it opens the Service Contract
 *           (C_Contract) window on a NEW record via the widget framework's
 *           value-changed channel (IsTabInNewMode) - the same open-in-new-mode
 *           path as VAS_121 New Customer / VAS_108 New Item / VAS_082 New GRN.
 *           New contracts are created through the real window (which persists
 *           via the MContract/X_C_Contract business-partner classes), never a
 *           hand-built insert form (Prompt_Instructions: "No Direct INSERT
 *           Queries"). No backend/controller: this tile only fires the open
 *           action.
 *           The value-changed channel only reaches a host window when the
 *           widget sits ON one. From the Home / landing dashboard (windowNo < 0)
 *           there is no host grid for that channel, so the Service Contract
 *           window (Export_ID VAS_1000262 / AD_Window_ID 1000248 - the same
 *           zoom target VAS_241 Contract search resolves to) is opened directly
 *           via VIS.viewManager.startWindow on a blank record instead, so the
 *           tile works on both hosts.
 * Design  - Design Specs/dashboard-widgets.md "Quick Action Widget" (matches
 *           VAS_121's borderless white-glass tile chrome - see that widget's
 *           2026-08-06 note). service-contracts-dashboard.html was not
 *           available when this was written; re-verify pixel details against it
 *           when available.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  # | Current Text     | Message Key
 * ---+------------------+--------------------------------
 *  1 | New Contract     | VAS_242_NewContract
 *  2 | Add a contract   | VAS_242_AddContract
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Service Contract window (Export_ID VAS_1000262), the same fixed
    // AD_Window_ID VAS_241 Contract search zooms to. Known directly, so the
    // Home-page path skips the by-name VAS_ZoomWindow/GetWindowId round-trip
    // VAS_121 needs for a window it only knows by display name.
    var ZOOM_TABLE = 'C_Contract';
    var ZOOM_WINDOW_ID = 1000248;

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

    VAS.VAS_242_NewContractWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas242-root">');

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

        /* Core-native "open on new record": a query flagged as a new-record query loads
           no rows ("2=3") and the core then auto-starts a blank record as soon as the
           tab finishes loading (same approach as VAS_121 / VAS_150 / VAS_082). */
        function buildNewRecordQuery() {
            try {
                var query = new VIS.Query(ZOOM_TABLE);
                query.addRestriction(VIS.Query.prototype.NEWRECORD);   // "2=3" -> loads no rows
                query.newRecord = true;
                if (query.setRecordCount) { query.setRecordCount(0); }
                return query;
            } catch (e) { return null; }
        }

        // Home / landing page (windowNo < 0): there is no host window for the
        // value-changed channel to put in new mode, so the Service Contract
        // window is started directly on a blank record. Best-effort - an
        // unavailable framework simply does not navigate.
        function openNewContractFromHome() {
            if (!window.VIS || !VIS.viewManager || typeof VIS.viewManager.startWindow !== 'function') { return; }
            try { VIS.viewManager.startWindow(ZOOM_WINDOW_ID, buildNewRecordQuery()); } catch (e) { /* best-effort */ }
        }

        // Open the widget's configured window (the Service Contract window)
        // directly on a NEW record through the widget framework's value-changed
        // channel. The host reuses the same window and starts a blank record
        // (IsTabInNewMode) - no duplicate window is opened. That channel only
        // reaches a host window when the widget sits ON one; from the Home page
        // the window is opened directly instead.
        function openNewContract() {
            try {
                if ($self.windowNo >= 0) {
                    var windowParam = {
                        "IsTabInNewMode": "true",
                        "TabIndex": "0"
                    };
                    $self.widgetFirevalueChanged(windowParam);
                }
                else {
                    openNewContractFromHome();
                }
            } catch (e) { /* best-effort */ }
        }

        function createWidget() {
            var title = lbl('VAS_242_NewContract', 'New Contract');
            var $card = $(
                '<button type="button" class="vas242-card" aria-label="' + escapeHtml(title) + '">' +
                    '<span class="vas242-well">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                    '</span>' +
                    '<span class="vas242-text">' +
                        '<span class="vas242-title">' + escapeHtml(title) + '</span>' +
                        '<span class="vas242-sub">' + escapeHtml(lbl('VAS_242_AddContract', 'Add a contract')) + '</span>' +
                    '</span>' +
                '</button>'
            );

            $card.on('click', function () { openNewContract(); });
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
    VAS.VAS_242_NewContractWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    /* The widget host registers itself here so the widget can drive the host. */
    VAS.VAS_242_NewContractWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_242_NewContractWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_242_NewContractWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_242_NewContractWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_242_NewContractWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
