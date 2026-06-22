/**
 * PO Fulfilment % Widget (Material Receipt / GRN dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing the overall purchase-order
 *           fulfilment percentage (delivered qty / ordered qty) across completed
 *           POs for the period, in success-green, with a "▲/▼ X% vs last month"
 *           point-change meta line. Not clickable, no icon.
 * Design  - design.md (Onfinity) "KPI And Summary Widget"; sizes in `em` against
 *           a widget-root clamp; label clamp anchored to --dash-inline-size.
 *           Namespaced vas-pof-*.
 *
 * Backend - VAS_085_POFulfilmentWidget/GetPOFulfilment
 * Summary Message Table: see Labels / Message Keys below.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text          | Message Key
 * ----+-----------------------+----------------------------------------
 *  1  | PO Fulfilment %       | VAS_POFulfilmentPct
 *  2  | vs last month         | VAS_VsLastMonthShort
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

    VAS.VAS_085_POFulfilmentWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-pof-root">');
        var $valueEl;
        var $metaEl;
        var $busy;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-pof-hidden', !show);
        }

        /* "▲ 7% vs last month" / "▼ 4% vs last month" / "0% vs last month".
           change is a percentage-point difference (current − last month). */
        function metaText(change) {
            var c = Number(change || 0);
            var suffix = lbl("VAS_VsLastMonthShort", "vs last month");
            if (c > 0) { return "▲ " + c + "% " + suffix; }
            if (c < 0) { return "▼ " + Math.abs(c) + "% " + suffix; }
            return "0% " + suffix;
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
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_085_POFulfilmentWidget/GetPOFulfilment',
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
            var current = Number(data.currentPercent || 0);
            var change = Number(data.changePercent || 0);

            if ($valueEl) {
                $valueEl.text(current + "%");
                $valueEl.attr('title', current + "%");
            }
            if ($metaEl) { $metaEl.text(metaText(change)); }
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
                '<div class="vas-pof-card vas-widget-bg">' +
                '<div class="vas-pof-label">' + escapeHtml(lbl("VAS_POFulfilmentPct", "PO Fulfilment %")) + '</div>' +
                '<div class="vas-pof-value">—</div>' +
                '<div class="vas-pof-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-pof-value');
            $metaEl = $card.find('.vas-pof-meta');

            $root.append($card);

            $busy = $('<div class="vas-pof-busy vas-pof-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_085_POFulfilmentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the label clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_085_POFulfilmentWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_085_POFulfilmentWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_085_POFulfilmentWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
