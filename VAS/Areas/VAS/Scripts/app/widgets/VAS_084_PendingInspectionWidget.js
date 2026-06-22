/**
 * Pending Inspection Widget (Material Receipt / GRN dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing the count of vendor-GRN receipt
 *           confirmation lines flagged for quality check but still awaiting their
 *           inspection result (on QA hold). Value in warning-amber with an
 *           "On QA hold" meta line. Not clickable, no icon.
 * Design  - design.md (Onfinity) "KPI And Summary Widget"; sizes in `em` against
 *           a widget-root clamp; label clamp anchored to --dash-inline-size.
 *           Namespaced vas-pinsp-*.
 *
 * Backend - VAS_084_PendingInspectionWidget/GetPendingInspection
 * Summary Message Table: see Labels / Message Keys below.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text          | Message Key
 * ----+-----------------------+----------------------------------------
 *  1  | Pending Inspection    | VAS_PendingInspection
 *  2  | On QA hold            | VAS_OnQAHold
 * ─────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the label
       clamp resolves against the dashboard's visible content area. A single
       document-level ResizeObserver serves every widget. */
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

    VAS.VAS_084_PendingInspectionWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-pinsp-root">');
        var $valueEl;
        var $metaEl;
        var $busy;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-pinsp-hidden', !show);
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        this.Initalize = function () {
            createWidget();
            $(document).on('vas-qaholds-updated.vas-pinsp', loadKpi);
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_084_PendingInspectionWidget/GetPendingInspection',
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

        function renderMetric(data) {
            var count = Number(data.pendingInspection || 0);

            if ($valueEl) {
                $valueEl.text(formatCount(count));
                $valueEl.attr('title', formatCount(count));
            }
            if ($metaEl) { $metaEl.text(lbl("VAS_OnQAHold", "On QA hold")); }
        }

        /* Error state — keep the tile + label, show a dash; never break the
           dashboard (design spec §States). */
        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) { $metaEl.text(''); }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-pinsp-card vas-widget-bg">' +
                '<div class="vas-pinsp-label">' + escapeHtml(lbl("VAS_PendingInspection", "Pending Inspection")) + '</div>' +
                '<div class="vas-pinsp-value">—</div>' +
                '<div class="vas-pinsp-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-pinsp-value');
            $metaEl = $card.find('.vas-pinsp-meta');

            $root.append($card);

            $busy = $('<div class="vas-pinsp-busy vas-pinsp-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('vas-qaholds-updated.vas-pinsp');
            $root.remove();
        };
    };

    VAS.VAS_084_PendingInspectionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the label clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_084_PendingInspectionWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_084_PendingInspectionWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_084_PendingInspectionWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
