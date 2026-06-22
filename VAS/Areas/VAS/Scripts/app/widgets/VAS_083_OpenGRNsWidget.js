/**
 * Open GRNs till Today Widget (Material Receipt / GRN dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing the count of vendor Goods
 *           Receipt Notes (M_InOut, MovementType 'V+', IsSOTrx 'N') that are
 *           still OPEN as of the server date. Clicking the tile drills through
 *           to the GRN window (VAS_MaterialReceipt) filtered to the exact same
 *           predicate that produced the count, so the landing row count equals
 *           the number shown.
 * Design  - design.md (Onfinity) "KPI And Summary Widget"; sizes in `em` against
 *           a widget-root clamp; title clamp anchored to --dash-inline-size.
 *           Namespaced vas-ogrn-*.
 *
 * Backend - VAS_083_OpenGRNsWidget/GetOpenGRNCount  (KPI count + drill-through date)
 * Summary Message Table: see Labels / Message Keys below.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+---------------------------------+--------------------------------
 *  1  | Open GRNs till Today            | VAS_OpenGRNsTillToday
 *  2  | Pending goods receipt posting   | VAS_PendingGRNPosting
 *  3  | No open GRNs                    | VAS_NoOpenGRNs
 *  4  | Couldn't load                   | VAS_CouldntLoad
 * ─────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
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

    /* Open-GRN DocStatus set — mirrors the backend count predicate so the
       drill-through filter is identical to what was counted. */
    var OPEN_STATUS_LIST = "'AP', 'IN', 'IP', 'NA', 'WC', 'WP'";

    VAS.VAS_083_OpenGRNsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-ogrn-root">');
        var $card;
        var $valueEl;
        var $metaEl;
        var $busy;

        /* Latest snapshot; drill-through reuses filterDateSql so the opened list
           matches the counted predicate exactly. */
        var lastCount = 0;
        var filterDateSql = "";

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-ogrn-hidden', !show);
        }

        function formatCount(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_083_OpenGRNsWidget/GetOpenGRNCount',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.error) { setError(); return; }

                    renderMetric(data || {});
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        /* Loaded / Zero states (design spec §9). */
        function renderMetric(data) {
            lastCount = Number(data.count || 0);
            filterDateSql = data.filterDateSql || "";

            if ($valueEl) {
                $valueEl.text(formatCount(lastCount));
                $valueEl.attr('title', formatCount(lastCount));
            }
            if ($metaEl) {
                $metaEl.text(lastCount === 0
                    ? lbl("VAS_NoOpenGRNs", "No open GRNs")
                    : lbl("VAS_PendingGRNPosting", "Pending goods receipt posting"));
            }
            if ($card) { $card.prop('disabled', false); }
        }

        /* Error state — keep the tile + label, show a dash, do not break the
           dashboard (design spec §9). */
        function setError() {
            lastCount = 0;
            filterDateSql = "";
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) { $metaEl.text(lbl("VAS_CouldntLoad", "Couldn't load")); }
            /* No data to drill into. */
            if ($card) { $card.prop('disabled', true); }
        }

        /* Drill-through: open the GRN window tab pre-filtered to the same
           predicate that produced the count. The widget sits on the GRN
           (Material Receipt) window landing page, so fire the value to the host
           tab in-place (Prompt_Instructions Scenario 1) rather than navigating
           away. DocStatus codes are ASCII, so plain quotes are portable across
           Oracle and PostgreSQL; the date literal is supplied dialect-correct by
           the backend (filterDateSql). */
        function openGrnList() {
            if (!filterDateSql) { return; }

            var where =
                "M_InOut.IsActive = 'Y'" +
                " AND M_InOut.IsSOTrx = 'N'" +
                " AND M_InOut.MovementType = 'V+'" +
                " AND M_InOut.DocStatus IN (" + OPEN_STATUS_LIST + ")" +
                " AND M_InOut.MovementDate < " + filterDateSql;

            var windowParam = {
                "TabWhereClause": where,
                "TabLayout": "N",   /* 'N' grid, 'Y' single, 'C' card */
                "TabIndex": "0"
            };

            $self.widgetFirevalueChanged(windowParam);
        }

        function createWidget() {
            $card = $(
                '<button type="button" class="vas-ogrn-card vas-widget-bg">' +
                '<div class="vas-ogrn-label">' + escapeHtml(lbl("VAS_OpenGRNsTillToday", "Open GRNs till Today")) + '</div>' +
                '<div class="vas-ogrn-value">—</div>' +
                '<div class="vas-ogrn-meta"></div>' +
                '</button>'
            );

            $valueEl = $card.find('.vas-ogrn-value');
            $metaEl = $card.find('.vas-ogrn-meta');

            $card.on('click', function () { openGrnList(); });

            $root.append($card);

            $busy = $('<div class="vas-ogrn-busy vas-ogrn-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    /* Relay a fired value (drill-through filter params) to the registered host. */
    VAS.VAS_083_OpenGRNsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    /* The widget host registers itself here so the widget can drive the host. */
    VAS.VAS_083_OpenGRNsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_083_OpenGRNsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_083_OpenGRNsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_083_OpenGRNsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_083_OpenGRNsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
