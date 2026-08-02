/**
 * VAS_178_NewMaterialIssueQuickAction
 * 1x1 quick action tile for Inventory Use dashboard.
 * Opens the Material Issue / Internal Use creation window directly on a new blank record.
 *
 * Summary Message Table
 *  # | Current Text                           | Message Key
 * ---+----------------------------------------+-----------------------------------
 *  1 | New Material Issue                     | VAS_NewMaterialIssue
 *  2 | Issue stock for production / spares    | VAS_IssueStockForProductionSpares
 *  3 | Unable to open the window              | VAS_CouldntOpenWindow
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

        // Open the Material Issue window on a NEW record via the widget framework's
        // value-changed channel (IsTabInNewMode), reusing the window instead of duplicate opening.
        function openMaterialIssueNewRecord() {
            if (isOpening) { return; }
            isOpening = true;

            if ($card) { $card.prop('disabled', true); }

            try {
                var windowParam = {
                    "IsTabInNewMode": "true",
                    "TabIndex": "0"
                };
                $self.widgetFirevalueChanged(windowParam);
            } catch (e) {
                // Direct fallback via window start
                openMaterialIssueWindowDirect();
            } finally {
                window.setTimeout(function () {
                    isOpening = false;
                    if ($card) { $card.prop('disabled', false); }
                }, 400);
            }
        }

        function openMaterialIssueWindowDirect() {
            if (materialIssueWindowId > 0) {
                startWindowById(materialIssueWindowId);
                return;
            }

            loadMaterialIssueWindowId(function (windowId) {
                if (windowId > 0) {
                    startWindowById(windowId);
                } else {
                    VIS.ADialog.error(
                        'VAS_CouldntOpenWindow',
                        true,
                        '',
                        label('VAS_CouldntOpenWindow', 'Unable to open the window.')
                    );
                }
            });
        }

        function buildNewRecordQuery() {
            var query = null;
            try {
                query = new VIS.Query("M_Inventory");
                query.addRestriction(VIS.Query.prototype.NEWRECORD);
                query.newRecord = true;
                if (query.setRecordCount) { query.setRecordCount(0); }
            } catch (e) { query = null; }
            return query;
        }

        function startWindowById(windowId) {
            var newRecordQuery = buildNewRecordQuery();
            if (VIS.viewManager && VIS.viewManager.startWindow) {
                VIS.viewManager.startWindow(windowId, newRecordQuery);
            } else if (VIS.AEnv && VIS.AEnv.startWindow) {
                VIS.AEnv.startWindow(windowId, newRecordQuery);
            }
        }

        function createWidget() {
            var title = label('VAS_NewMaterialIssue', 'New Material Issue');
            var subtitle = label('VAS_IssueStockForProductionSpares', 'Issue stock for production / spares');

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

        this.Initalize = function () {
            createWidget();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($card) { $card.off('click'); }
            $root.remove();
        };
    };

    VAS.VAS_178_NewMaterialIssueQuickAction.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
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
