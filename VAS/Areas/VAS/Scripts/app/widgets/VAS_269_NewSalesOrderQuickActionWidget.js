/**
 * VAS_269 New Sales Order Quick Action Widget (Sales Order dashboard)
 * Purpose - 1x1 quick-action tile. Clicking it (or Enter/Space while
 *           focused) opens the Sales Order (C_Order) window directly on a
 *           NEW record - the same open-in-new-mode path as VAS_121 New
 *           Customer / VAS_108 New Item / VAS_082 New GRN / VAS_242 New
 *           Contract. No quotation picker, no wizard, no data query, no
 *           writes: per the 02_New_Sales_Order_Quick_Action_Claude_
 *           Development_Prompt.txt override, the draft's multi-step
 *           quotation-to-Sales-Order wizard is explicitly NOT implemented.
 *           This tile only fires the open action; the real window (header
 *           tab AD_Tab_ID 1002552 / C_Order, child tab AD_Tab_ID 1002549 /
 *           C_OrderLine) owns record creation. No backend/controller -
 *           client-side only.
 * Design  - Follows VAS_242 New Contract's tile pattern exactly (per an
 *           explicit later design-alignment request): borderless white-glass
 *           tile chrome - no dashed border/pale fill - with a centred blue
 *           "+" well, a bold title, and a muted subtitle line beneath it.
 *           CSS namespaced vas269-* (Prompt_Instructions MPC prefix rule).
 *           Sizing follows the shared --dash-inline-size convention used by
 *           the other dashboard quick-action tiles.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  # | Current Text        | Message Key
 * ---+---------------------+--------------------------------
 *  1 | New Sales Order     | VAS_269_NewSalesOrder
 *  2 | Add a sales order   | VAS_269_AddSalesOrder
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Target window for the Home / landing page (windowNo < 0), where the
       framework's value-changed channel has no host window to open in new
       mode. The AD_Window_ID is resolved from this name by VAS.ZoomUtil
       (same window VAS_268 Global Sales Search targets for "Open Record"). */
    var ZOOM_TABLE = 'C_Order';
    var ZOOM_WINDOW_NAME_NEW = 'VAS_SalesOrder';
    var ZOOM_WINDOW_NAME_OLD = 'VAS_SalesOrder';

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

    VAS.VAS_269_NewSalesOrderQuickActionWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas269-root">');
        var zoomWindowId = 0;

        function lbl(key, fallback) {
            return VIS.Msg.getMsg(key);
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
           no rows and the core then auto-starts a blank record as soon as the tab
           finishes loading (same approach as VAS_121 / VAS_150 / VAS_082). */
        function buildNewRecordQuery() {
            try {
                var query = new VIS.Query(ZOOM_TABLE);
                query.addRestriction(VIS.Query.prototype.NEWRECORD);   // "2=3" -> loads no rows
                query.newRecord = true;
                if (query.setRecordCount) { query.setRecordCount(0); }
                return query;
            } catch (e) { return null; }
        }

        /* Home / landing page (windowNo < 0): there is no host window for the
           value-changed channel to put in new mode, so the Sales Order window is
           resolved by name and started on a blank record. Best-effort - an
           unresolved window or an unavailable framework simply does not navigate. */
        function openNewSalesOrderFromHome() {
            if (!window.VAS || !VAS.ZoomUtil) { return; }

            if (zoomWindowId > 0) {
                openWindow(zoomWindowId);
                return;
            }

            VAS.ZoomUtil.getWindowId(ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                .done(function (id) {
                    id = Number(id) || 0;
                    if (id <= 0) { return; }
                    zoomWindowId = id;
                    openWindow(id);
                });
        }

        function openWindow(id) {
            if (!window.VIS || !VIS.viewManager || typeof VIS.viewManager.startWindow !== 'function') { return; }
            try { VIS.viewManager.startWindow(id, buildNewRecordQuery()); } catch (e) { /* best-effort */ }
        }

        // Open the Sales Order window directly on a NEW record. When this
        // tile is hosted on a window, reuse the widget framework's
        // value-changed channel (IsTabInNewMode) so the host opens the
        // header tab (TabIndex 0 / AD_Tab_ID 1002552) in new-record mode
        // without a duplicate window. From the Home page (no host window to
        // relay through) the window is opened directly instead.
        function openNewSalesOrder() {
            try {
                if ($self.windowNo >= 0) {
                    var windowParam = {
                        "IsTabInNewMode": "true",
                        "TabIndex": "0"
                    };
                    $self.widgetFirevalueChanged(windowParam);
                }
                else {
                    openNewSalesOrderFromHome();
                }
            } catch (e) { /* best-effort */ }
        }

        function createWidget() {
            var title = lbl('VAS_269_NewSalesOrder', 'New Sales Order');
            var $card = $(
                '<button type="button" class="vas269-card" aria-label="' + escapeHtml(title) + '">' +
                    '<span class="vas269-well">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                    '</span>' +
                    '<span class="vas269-text">' +
                        '<span class="vas269-title">' + escapeHtml(title) + '</span>' +
                        '<span class="vas269-sub">' + escapeHtml(lbl('VAS_269_AddSalesOrder', 'Add a sales order')) + '</span>' +
                    '</span>' +
                '</button>'
            );

            $card.on('click', function () { openNewSalesOrder(); });
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
    VAS.VAS_269_NewSalesOrderQuickActionWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    /* The widget host registers itself here so the widget can drive the host. */
    VAS.VAS_269_NewSalesOrderQuickActionWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_269_NewSalesOrderQuickActionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_269_NewSalesOrderQuickActionWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_269_NewSalesOrderQuickActionWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_269_NewSalesOrderQuickActionWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
