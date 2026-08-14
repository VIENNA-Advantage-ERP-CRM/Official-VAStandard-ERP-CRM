/**
 * VAS_178_NewMaterialIssueQuickAction
 * 1x1 quick action tile for Inventory Use dashboard.
 * Opens the Material Issue / Internal Use creation window directly on a new blank record.
 *
 * Summary Message Table
 *  # | Current Text                           | Message Key
 * ---+----------------------------------------+-----------------------------------
 *  1 | New Material Issue                     | VAS_178_NewMaterialIssue
 *  2 | Issue stock for production / spares    | VAS_178_IssueStockForProductionSpares
 *  3 | Unable to open the window              | VAS_178_CouldntOpenWindow
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

    VAS.VAS_178_NewMaterialIssueQuickAction = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-178-nmi-root">');
        var $card;
        var isOpening = false;
        var materialIssueWindowId = 0;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
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
            setupResizeObserver();
            loadMaterialIssueWindowId(null);
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                var ro = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                ro.observe($root[0]);
            } catch (e) { /* fallback to css clamps */ }
        }

        function loadMaterialIssueWindowId(onReady) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_178_NewMaterialIssueQuickAction/GetMaterialIssueWindowId',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = parseResponse(response);
                    materialIssueWindowId = Number((result && result.windowId) || 0);
                    if (onReady) { onReady(materialIssueWindowId); }
                },
                error: function () {
                    if (onReady) { onReady(0); }
                }
            });
        }

        /* The Internal Use window must open on a NEW record and must never be duplicated.
           viewManager.startWindow only reuses windows in its closed-window cache;
           an already-OPEN window gets a second instance on every click. So:
           - if the Internal Use window this widget opened is still open, bring that SAME
             window to the front and start another new record in it (cmd_new);
           - otherwise open it once with a "new record" query so the CORE itself
             auto-starts the blank record when the tab loads (buildNewRecordQuery).
           The opened-window reference is kept on a WINDOW-GLOBAL (keyed by window
           id), not on widget-instance state, so it survives the dashboard
           re-creating the widget - otherwise the reference resets and a new window
           opens every time. */
        var MI_VIEW_STORE = '__vasMaterialIssueViews';

        function rememberMiView(windowId, view) {
            if (!window[MI_VIEW_STORE]) { window[MI_VIEW_STORE] = {}; }
            window[MI_VIEW_STORE][windowId] = view || null;
        }

        function recallMiView(windowId) {
            return window[MI_VIEW_STORE] ? window[MI_VIEW_STORE][windowId] : null;
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
           as the tab finishes loading. No onLoad/cmd_new timing needed - this is
           the same path the core itself uses for zoom-to-new-record. */
        function buildNewRecordQuery() {
            var query = null;
            try {
                query = new VIS.Query("M_Inventory");
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
            // Reuse the window we already opened, if it is still open: focus it
            // and start a new record in it - no second window.
            var existing = recallMiView(windowId);
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

            rememberMiView(windowId, view);
        }

        // Open the widget's configured window (the Internal Use / Material Issue window)
        // directly on a NEW record through the widget framework's value-changed
        // channel. The host reuses the same window and starts a blank record
        // (IsTabInNewMode) - no duplicate window is opened.
        function openMaterialIssueNewRecord() {
            if (isOpening) { return; }
            isOpening = true;

            if ($card) { $card.prop('disabled', true); }

            try {
                var windowParam = {
                    "IsTabInNewMode": "true",
                    "TabIndex": "0"
                };
                // widgetFirevalueChanged returns false when no host listener is attached
                // (it never throws), so the return flag - not an exception - is what tells
                // us whether the framework channel handled the click.
                if (!$self.widgetFirevalueChanged(windowParam)) {
                    openMaterialIssueWindow();
                }
            } finally {
                window.setTimeout(function () {
                    isOpening = false;
                    if ($card) { $card.prop('disabled', false); }
                }, 400);
            }
        }

        function openMaterialIssueWindow() {
            if (materialIssueWindowId > 0) {
                startWindowById(materialIssueWindowId);
                return;
            }
            loadMaterialIssueWindowId(function (windowId) {
                if (windowId > 0) {
                    startWindowById(windowId);
                }
                else {
                    VIS.ADialog.error(
                        'VAS_178_CouldntOpenWindow',
                        true,
                        '',
                        label('VAS_178_CouldntOpenWindow', 'Unable to open the window.')
                    );
                }
            });
        }

        function createWidget() {
            var title = label('VAS_178_NewMaterialIssue', 'New Material Issue');
            var subtitle = label('VAS_178_IssueStockForProductionSpares', 'Issue stock for production / spares');

            $card = $(
                '<button type="button" class="vas-178-nmi-card vas-widget-bg" aria-label="' + escapeHtml(title + '. ' + subtitle) + '">' +
                '<div class="vas-178-nmi-iconrow">' +
                '<span class="vas-178-nmi-ico" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                '</span>' +
                '</div>' +
                '<div class="vas-178-nmi-text">' +
                '<div class="vas-178-nmi-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-178-nmi-sub">' + escapeHtml(subtitle) + '</div>' +
                '</div>' +
                '</button>'
            );

            $card.on('click', function () { openMaterialIssueNewRecord(); });
            $root.append($card);
        }

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($card) { $card.off('click'); }
            $root.remove();
        };
    };

    // Returns true only when a host listener consumed the value, so the caller can
    // fall back to opening the window directly instead of failing silently.
    VAS.VAS_178_NewMaterialIssueQuickAction.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener && this.listener.widgetFirevalueChanged) {
            this.listener.widgetFirevalueChanged(value);
            return true;
        }
        return false;
    };

    VAS.VAS_178_NewMaterialIssueQuickAction.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_178_NewMaterialIssueQuickAction.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_178_NewMaterialIssueQuickAction.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_178_NewMaterialIssueQuickAction.prototype.refreshWidget = function () { };

    VAS.VAS_178_NewMaterialIssueQuickAction.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
