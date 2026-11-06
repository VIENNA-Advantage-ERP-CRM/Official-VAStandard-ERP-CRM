/**
 * VAS_306_NewRequisitionWidget - 1x1 quick action on the Requisition screen (and its Home copy).
 * Opens the Requisition window on a NEW record. Same contract as VAS_204_NewPurchaseOrderWidget:
 * on the window it goes through the widget framework value change (IsTabInNewMode), on Home it
 * resolves the window by name (VAS.ZoomUtil, window VAS_Requisition / Requisition) and opens it
 * with a new-record query; a window it opened earlier is reused instead of opening a second one.
 * No own backend (window id via VAS/VAS_ZoomWindow/GetWindowId).
 *
 * Labels / Message Keys
 *  VAS_306_NewRequisition       New Requisition
 *  VAS_306_RequestItems         Request items for a warehouse
 *  VAS_306_CouldntOpenWindow    Unable to open the Requisition window.
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;
    var VIEW_STORE = '__vasReqViews';

    VAS.VAS_306_NewRequisitionWidget = function () {
        var self = this;
        var $root = $('<div class="vas-rqw-root">');
        var $card = null;
        this.frame = null;
        this.windowNo = -1;

        function newRecordQuery() {
            try {
                var q = new VIS.Query("M_Requisition");
                q.addRestriction(VIS.Query.prototype.NEWRECORD);
                q.newRecord = true;
                if (q.setRecordCount) { q.setRecordCount(0); }
                return q;
            } catch (e) { return null; }
        }

        function viewOpen(view) {
            if (!view || !view.getId) { return false; }
            var li = document.getElementById(String(view.getId()));
            return !!(li && li.tagName === 'LI');
        }

        function openOnHome() {
            VAS.ZoomUtil.getWindowId('VAS_Requisition', 'Requisition').done(function (windowId) {
                if (!(windowId > 0)) {
                    VIS.ADialog.error('VAS_306_CouldntOpenWindow', true, '', U.lbl('VAS_306_CouldntOpenWindow', 'Unable to open the Requisition window.'));
                    return;
                }
                var store = window[VIEW_STORE] = window[VIEW_STORE] || {};
                var existing = store[windowId];
                if (existing && viewOpen(existing)) {
                    try {
                        var id = existing.getId();
                        if (VIS.desktopMgr && VIS.desktopMgr.toggleContainer) { VIS.desktopMgr.toggleContainer(id); }
                        if (VIS.desktopMgr && VIS.desktopMgr.activateTaskBarItemUsingID) {
                            VIS.desktopMgr.activateTaskBarItemUsingID({ data: function () { return id; } });
                        }
                        if (existing.cPanel && existing.cPanel.cmd_new) { existing.cPanel.cmd_new(false); }
                        return;
                    } catch (e) { /* open again below */ }
                }
                store[windowId] = VIS.viewManager.startWindow(windowId, newRecordQuery());
            });
        }

        this.Initalize = function () {
            var title = U.lbl('VAS_306_NewRequisition', 'New Requisition');
            $card = $('<button type="button" class="vas-rqw-qa" aria-label="' + U.esc(title) + '">' +
                '<span class="plus">' + U.icon.plus + '</span>' +
                '<span><span class="qt">' + U.esc(title) + '</span><span class="qs">' + U.esc(U.lbl('VAS_306_RequestItems', 'Request items for a warehouse')) + '</span></span>' +
                '</button>');
            $root.empty().append($card);
            $card.on('click', function () {
                this.blur();
                if (self.windowNo >= 0 && self.listener && typeof self.listener.widgetFirevalueChanged === 'function') {
                    self.widgetFirevalueChanged({ "IsTabInNewMode": "true", "TabIndex": "0" });
                    return;
                }
                openOnHome();
            });
        };

        this.reload = function () { };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () { if ($card) { $card.off(); } $root.remove(); };
    };

    U.makeWidget(VAS.VAS_306_NewRequisitionWidget);
})(VAS, jQuery);
