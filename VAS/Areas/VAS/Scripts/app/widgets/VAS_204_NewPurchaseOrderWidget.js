/**
 * VAS_204_NewPurchaseOrderWidget
 * 1x1 Quick Action Tile for the Purchase Order Dashboard.
 *
 * Clicking the tile navigates straight to the Purchase Order window on a NEW
 * record. It deliberately opens no picker and no modal - this widget is a direct
 * counterpart of VAS_082_NewGRNWidget and follows the same open-on-new-record
 * contract and the same tile design.
 *
 * Backend - VAS_204_NewPurchaseOrderWidget/GetPurchaseOrderWindowId
 *
 * Labels / Message Keys
 *  # | Current Text                              | Message Key
 * ---+-------------------------------------------+----------------------------------
 *  1 | New Purchase Order                        | VAS_204_NewPurchaseOrder
 *  2 | Raise a purchase order                    | VAS_204_RaisePurchaseOrder
 *  3 | Unable to open the Purchase Order window. | VAS_204_CouldntOpenWindow
 */
(function (VAS, $) {
    "use strict";

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

    VAS.VAS_204_NewPurchaseOrderWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-npo-root">');
        var $card = null;
        var poWindowId = 0;

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
            loadPoWindowId(null);
        };

        function loadPoWindowId(onReady) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_204_NewPurchaseOrderWidget/GetPurchaseOrderWindowId',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = parseResponse(response);
                    poWindowId = Number((result && result.windowId) || 0);
                    if (onReady) { onReady(poWindowId); }
                },
                error: function () {
                    if (onReady) { onReady(0); }
                }
            });
        }

        /* The Purchase Order window must open on a NEW record and must never be
           duplicated. viewManager.startWindow only reuses windows in its closed-window
           cache; an already-OPEN window gets a second instance on every click. So:
           - if the PO window this widget opened is still open, bring that SAME window
             to the front and start another new record in it (cmd_new);
           - otherwise open it once with a "new record" query so the CORE itself
             auto-starts the blank record when the tab loads (buildNewRecordQuery).
           The opened-window reference is kept on a WINDOW-GLOBAL (keyed by window id),
           not on widget-instance state, so it survives the dashboard re-creating the
           widget - otherwise the reference resets and a new window opens every time. */
        var PO_VIEW_STORE = '__vasPoViews';

        function rememberPoView(windowId, view) {
            if (!window[PO_VIEW_STORE]) { window[PO_VIEW_STORE] = {}; }
            window[PO_VIEW_STORE][windowId] = view || null;
        }

        function recallPoView(windowId) {
            return window[PO_VIEW_STORE] ? window[PO_VIEW_STORE][windowId] : null;
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
           loads no rows ("2=3"), and GridController.queryCompleted -> checkInsertNewRow()
           then auto-starts a blank record (dataNew) as soon as the tab finishes loading.
           No onLoad/cmd_new timing needed - this is the same path the core itself uses
           for zoom-to-new-record. */
        function buildNewRecordQuery() {
            var query = null;
            try {
                query = new VIS.Query("C_Order");
                query.addRestriction(VIS.Query.prototype.NEWRECORD); // "2=3" -> loads no rows
                // addRestriction only auto-flags against the static VIS.Query.NEWRECORD,
                // which this core never assigns (only the prototype constant exists),
                // so set the flag checkInsertNewRow() reads directly.
                query.newRecord = true;
                if (query.setRecordCount) { query.setRecordCount(0); }
            } catch (e) { query = null; }
            return query;
        }

        function startWindowById(windowId) {
            // Reuse the window we already opened, if it is still open: focus it and
            // start a new record in it - no second window.
            var existing = recallPoView(windowId);
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

            rememberPoView(windowId, view);
        }

        // Open the widget's configured window (the Purchase Order window) directly on
        // a NEW record through the widget framework's value-changed channel. The host
        // reuses the same window and starts a blank record (IsTabInNewMode) - no
        // duplicate window is opened.
        function openPoNewRecord() {
            if ($self.listener && typeof $self.listener.widgetFirevalueChanged === 'function') {
                $self.widgetFirevalueChanged({
                    "IsTabInNewMode": "true",
                    "TabIndex": "0"
                });
                return;
            }
            // No dashboard listener attached: open the window directly instead.
            openPoWindow();
        }

        function openPoWindow() {
            if (poWindowId > 0) {
                startWindowById(poWindowId);
                return;
            }
            loadPoWindowId(function (windowId) {
                if (windowId > 0) {
                    startWindowById(windowId);
                }
                else {
                    VIS.ADialog.error(
                        'VAS_204_CouldntOpenWindow',
                        true,
                        '',
                        lbl('VAS_204_CouldntOpenWindow', 'Unable to open the Purchase Order window.')
                    );
                }
            });
        }

        function createWidget() {
            var title = lbl("VAS_204_NewPurchaseOrder", "New Purchase Order");
            var sub = lbl("VAS_204_RaisePurchaseOrder", "Raise a purchase order");

            $card = $(
                '<button type="button" class="vas-npo-card vas-widget-bg" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-npo-iconrow">' +
                '<span class="vas-npo-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                '</span>' +
                '</div>' +
                '<div class="vas-npo-text">' +
                '<div class="vas-npo-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-npo-sub">' + escapeHtml(sub) + '</div>' +
                '</div>' +
                '</button>'
            );

            // Open the Purchase Order window on a NEW record via the widget framework's
            // value-changed channel (IsTabInNewMode), reusing the same window instead of
            // opening a duplicate each click.
            $card.on('click', function () {
                // Opening the PO window returns focus to this button afterwards, which
                // would leave a focus ring on the tile. Drop focus for pointer users;
                // keyboard activation keeps it (:focus-visible still applies).
                this.blur();
                openPoNewRecord();
            });
            $root.append($card);
        }

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if ($card) { $card.off('click'); }
            $root.remove();
        };
    };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.refreshWidget = function () { };

    VAS.VAS_204_NewPurchaseOrderWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
