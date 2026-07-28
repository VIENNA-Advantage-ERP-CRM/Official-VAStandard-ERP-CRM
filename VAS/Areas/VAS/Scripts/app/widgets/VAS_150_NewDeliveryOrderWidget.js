/**
 * New Delivery Order Widget (Delivery Order dashboard)
 * Widget number 150.
 * Widget size: 1 column x 1 row (quick-action tile).
 * A one-tap launcher: clicking the tile opens the outbound customer Delivery
 * Order (Shipment) window on a NEW record so the user can pick a customer /
 * sales order, allocate stock and confirm a DO through the real core flow.
 * The tile creates no document itself. The target window is resolved by NAME
 * on the server (VAS_DeliveryOrder, else the core "Shipment (Customer)"),
 * never by a hardcoded id. The window this tile opened is reused on repeat
 * clicks (focused + a fresh new record started) instead of duplicated.
 * Backend - VAS_150_NewDeliveryOrderWidget/GetDeliveryWindowId
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+----------------------------------------+------------------------
 *  1 | New Delivery Order                    | VAS_150_NDO_Title
 *  2 | Create an outbound DO                 | VAS_150_NDO_Sub
 *  3 | Unable to open the Delivery Order window. | VAS_150_NDO_CouldntOpenWindow
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

    VAS.VAS_150_NewDeliveryOrderWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-ndo-root">');
        var deliveryWindowId = 0;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        this.Initalize = function () {
            createWidget();
            loadDeliveryWindowId(null);
        };

        function loadDeliveryWindowId(onReady) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_150_NewDeliveryOrderWidget/GetDeliveryWindowId',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = parseResponse(response);
                    deliveryWindowId = Number((result && result.windowId) || 0);
                    if (onReady) { onReady(deliveryWindowId); }
                },
                error: function () {
                    if (onReady) { onReady(0); }
                }
            });
        }

        /* The DO window must open on a NEW record and must never be duplicated.
           viewManager.startWindow only reuses windows in its closed-window cache;
           an already-OPEN window gets a second instance on every click. So:
           - if the DO window this widget opened is still open, bring that SAME
             window to the front and start another new record in it (cmd_new);
           - otherwise open it once with a "new record" query so the CORE itself
             auto-starts the blank record when the tab loads (buildNewRecordQuery).
           The opened-window reference is kept on a WINDOW-GLOBAL (keyed by window
           id), not on widget-instance state, so it survives the dashboard
           re-creating the widget. */
        var DO_VIEW_STORE = '__vasDeliveryViews';

        function rememberDeliveryView(windowId, view) {
            if (!window[DO_VIEW_STORE]) { window[DO_VIEW_STORE] = {}; }
            window[DO_VIEW_STORE][windowId] = view || null;
        }

        function recallDeliveryView(windowId) {
            return window[DO_VIEW_STORE] ? window[DO_VIEW_STORE][windowId] : null;
        }

        function startNewRecordIn(view) {
            try {
                if (view && view.cPanel && view.cPanel.cmd_new) {
                    view.cPanel.cmd_new(false);
                    return true;
                }
            } catch (e) { /* window still open; user can press New */ }
            return false;
        }

        // The taskbar LI carries the view id and is removed on close, so its
        // presence in the DOM means that window is still open.
        function isViewStillOpen(view) {
            if (!view || !view.getId) { return false; }
            var li = document.getElementById(String(view.getId()));
            return !!(li && li.tagName === 'LI');
        }

        function focusView(view) {
            var viewId = view.getId();
            var li = document.getElementById(String(viewId));
            if (li && $(li).hasClass('vis-app-f-selected')) { return; }
            if (VIS.desktopMgr && VIS.desktopMgr.toggleContainer) {
                VIS.desktopMgr.toggleContainer(viewId);
            }
            if (VIS.desktopMgr && VIS.desktopMgr.activateTaskBarItemUsingID) {
                VIS.desktopMgr.activateTaskBarItemUsingID({ data: function () { return viewId; } });
            }
        }

        /* Core-native "open on new record": a query flagged as a new-record query
           loads no rows ("2=3"), and GridController.queryCompleted ->
           checkInsertNewRow() then auto-starts a blank record (dataNew) as soon
           as the tab finishes loading. */
        function buildNewRecordQuery() {
            var query = null;
            try {
                query = new VIS.Query("M_InOut");
                query.addRestriction(VIS.Query.prototype.NEWRECORD); // "2=3" -> loads no rows
                query.newRecord = true;
                if (query.setRecordCount) { query.setRecordCount(0); }
            } catch (e) { query = null; }
            return query;
        }

        function startWindowById(windowId) {
            // Reuse the window we already opened, if it is still open: focus it
            // and start a new record in it - no second window.
            var existing = recallDeliveryView(windowId);
            if (existing && isViewStillOpen(existing)) {
                try {
                    focusView(existing);
                    startNewRecordIn(existing);
                    return;
                } catch (e) { /* fall through and open it again */ }
            }

            var newRecordQuery = buildNewRecordQuery();

            var view = null;
            if (VIS.viewManager && VIS.viewManager.startWindow) {
                view = VIS.viewManager.startWindow(windowId, newRecordQuery);
            }
            else if (VIS.AEnv && VIS.AEnv.startWindow) {
                view = VIS.AEnv.startWindow(windowId, newRecordQuery);
            }

            rememberDeliveryView(windowId, view);
        }

        function openDeliveryWindow() {
            if (deliveryWindowId > 0) {
                startWindowById(deliveryWindowId);
                return;
            }
            loadDeliveryWindowId(function (windowId) {
                if (windowId > 0) {
                    startWindowById(windowId);
                }
                else {
                    VIS.ADialog.error(
                        'VAS_150_NDO_CouldntOpenWindow',
                        true,
                        '',
                        lbl('VAS_150_NDO_CouldntOpenWindow', 'Unable to open the Delivery Order window.')
                    );
                }
            });
        }

        function createWidget() {
            var $card = $(
                '<button type="button" class="vas-ndo-card vas-widget-bg" aria-label="' + escapeHtml(lbl("VAS_150_NDO_Title", "New Delivery Order")) + '">' +
                '<div class="vas-ndo-iconrow">' +
                '<span class="vas-ndo-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                '</span>' +
                '</div>' +
                '<div class="vas-ndo-text">' +
                '<div class="vas-ndo-title">' + escapeHtml(lbl("VAS_150_NDO_Title", "New Delivery Order")) + '</div>' +
                '<div class="vas-ndo-sub">' + escapeHtml(lbl("VAS_150_NDO_Sub", "Create an outbound DO")) + '</div>' +
                '</div>' +
                '</button>'
            );

            $card.on('click', function () { openDeliveryWindow(); });
            $root.append($card);
        }

        this.refreshWidget = function () { };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_150_NewDeliveryOrderWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_150_NewDeliveryOrderWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_150_NewDeliveryOrderWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_150_NewDeliveryOrderWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_150_NewDeliveryOrderWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_150_NewDeliveryOrderWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
