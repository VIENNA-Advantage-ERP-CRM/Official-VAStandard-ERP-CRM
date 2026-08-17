/**
 * VAS_182_SparesConsumablesIssuesWidget
 * 2x1 KPI tile for Inventory Use dashboard.
 * Displays percentage share of material issue value for Spares / Consumables Month-to-Date (MTD).
 *
 * Summary Message Table
 *  # | Current Text                    | Message Key
 * ---+---------------------------------+-----------------------------------
 *  1 | Spares / Consumables            | VAS_SparesConsumables
 *  2 | Of issued value MTD             | VAS_OfIssuedValueMTD
 *  3 | Couldn't load                   | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

/* 🛑 🚨 🚨 🛑  DEMO DATA - UI PREVIEW ONLY - DELETE BEFORE COMMIT  🛑 🚨 🚨 🛑
   Set to false (or delete this line and the `if (VAS_182_DEMO)` guard in loadKpi). */
var VAS_182_DEMO = true;

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

    VAS.VAS_182_SparesConsumablesIssuesWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-sci-root">');
        var $card;
        var $valueEl;
        var $metaEl;
        var $busy;

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

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-sci-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadKpi();
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
            } catch (e) { }
        }

        function loadKpi() {
            showBusy(true);

            /* 🛑 🚨 DEMO DATA - UI PREVIEW ONLY - DELETE BEFORE COMMIT 🚨 🛑
               This tile renders a single percentage, so there are no rows to fake; the value below
               just replaces the live 0% while the classification question is open. */
            if (VAS_182_DEMO) {
                renderMetric({ percentage: 39 });
                showBusy(false);
                return;
            }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_182_SparesConsumablesIssuesWidget/GetSparesConsumablesPercentage',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) { setError(); return; }
                    renderMetric(data);
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        function renderMetric(data) {
            var pct = Number(data.percentage || 0);

            if ($valueEl) {
                $valueEl.text(pct + '%');
                $valueEl.attr('title', pct + '%');
            }
            if ($metaEl) {
                $metaEl.text(label("VAS_OfIssuedValueMTD", "Of issued value MTD"));
            }
            if ($card) { $card.prop('disabled', false); }
        }

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) { $metaEl.text(label("VAS_CouldntLoad", "Couldn't load")); }
            if ($card) { $card.prop('disabled', true); }
        }

        // The framework navigates IN-PLACE (no new window, no half-drawn screen) only when the
        // payload's ActionName equals the name of the window currently HOSTING this widget;
        // otherwise VIS.dynamicWidget resolves ActionName through UserPreference/GetWindowID and
        // opens a second window. Resolve the host window name from the listener chain.
        // Established pattern - see VAS_091_MaterialReceiptSearchWidget.js zoomTo() (also 067/069/070/128).
        function hostWindowName() {
            try {
                var l = $self.listener;
                for (var i = 0; i < 6 && l; i++) {
                    if (l.apanel && l.apanel.gridWindow && l.apanel.gridWindow.getName) {
                        return l.apanel.gridWindow.getName();
                    }
                    if (l.gridWindow && l.gridWindow.getName) {
                        return l.gridWindow.getName();
                    }
                    l = l.listener;
                }
            } catch (e) { }
            return '';
        }

        // Drill to the EXACT documents behind the KPI: completed internal-use documents for the
        // current month that carry at least one spares / consumables line. The EXISTS predicate
        // mirrors the line-level classification in GetSparesConsumablesPercentageData() one-for-one,
        // so the list can never drift from the percentage on the tile.
        // Portability: only columns present on every target DB are used here - the work-order
        // columns (VA075_WorkOrder_ID / VAMFG_M_WorkOrder_ID) are module-specific and absent on
        // DB 1, and an unresolved column makes the grid query throw instead of opening.
        function openSparesConsumablesList() {
            var where =
                "M_Inventory.IsActive = 'Y'" +
                " AND M_Inventory.IsInternalUse = 'Y'" +
                " AND M_Inventory.DocStatus IN ('CO', 'CL')" +
                " AND M_Inventory.MovementDate >= TRUNC(SYSDATE, 'MM')" +
                " AND M_Inventory.MovementDate < ADD_MONTHS(TRUNC(SYSDATE, 'MM'), 1)" +
                " AND EXISTS (SELECT 1 FROM M_InventoryLine scl" +
                " WHERE scl.M_Inventory_ID = M_Inventory.M_Inventory_ID" +
                " AND scl.IsActive = 'Y'" +
                " AND scl.C_Charge_ID IS NULL" +
                " AND scl.M_RequisitionLine_ID IS NULL)";

            $self.widgetFirevalueChanged({
                "TabWhereClause": where,
                "TabLayout": "Y",
                "TabIndex": "0",
                "ActionName": hostWindowName() || "VAS_InternalUseInventory",
                "ActionType": "W"
            });
        }

        function createWidget() {
            var title = label("VAS_SparesConsumables", "Spares / Consumables");
            $card = $(
                '<button type="button" class="vas-sci-card vas-widget-bg" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-sci-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-sci-value">—</div>' +
                '<div class="vas-sci-meta"></div>' +
                '</button>'
            );

            $valueEl = $card.find('.vas-sci-value');
            $metaEl = $card.find('.vas-sci-meta');

            $card.on('click', function () { openSparesConsumablesList(); });
            $root.append($card);

            $busy = $('<div class="vas-sci-busy vas-sci-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($card) { $card.off('click'); }
            $root.remove();
        };
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
