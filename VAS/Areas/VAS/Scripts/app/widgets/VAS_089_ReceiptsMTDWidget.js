/**
 * Receipts MTD Widget (Material Receipt / GRN dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing the count of vendor material
 *           receipts (M_InOut, MovementType 'V+', IsSOTrx 'N') for the current
 *           month-to-date, in success-green, with a "vs N last month" comparison
 *           meta line. Not clickable, no icon.
 * Design  - design.md (Onfinity) "KPI And Summary Widget"; sizes in `em` against
 *           a widget-root clamp; label clamp anchored to --dash-inline-size.
 *           Namespaced vas-rmtd-*.
 *
 * Backend - VAS_089_ReceiptsMTDWidget/GetReceiptsMTD
 * Summary Message Table: see Labels / Message Keys below.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text          | Message Key
 * ----+-----------------------+----------------------------------------
 *  1  | Receipts MTD          | VAS_089_ReceiptsMTD
 *  2  | vs {0} last month     | VAS_089_VsLastMonth
 * ─────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget. */
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

    VAS.VAS_089_ReceiptsMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-rmtd-root">');
        var $valueEl;
        var $metaEl;
        var $busy;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-rmtd-hidden', !show);
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }

        /* "vs {0} last month" — reuse the message template's {0} placeholder when
           present, else fall back to a plain composed string. */
        function metaText(prevCount) {
            var n = formatCount(prevCount);
            var tmpl = lbl("VAS_089_VsLastMonth", "vs {0} last month");
            return tmpl.indexOf("{0}") >= 0 ? tmpl.replace("{0}", n) : ("vs " + n + " last month");
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
                url: VIS.Application.contextUrl + 'VAS_089_ReceiptsMTDWidget/GetReceiptsMTD',
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
            var mtd = Number(data.receiptsMtd || 0);
            var prev = Number(data.receiptsPrevMonth || 0);

            if ($valueEl) {
                $valueEl.text(formatCount(mtd));
                $valueEl.attr('title', formatCount(mtd));
            }
            if ($metaEl) { $metaEl.text(metaText(prev)); }
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
                '<div class="vas-rmtd-card vas-widget-bg">' +
                '<div class="vas-rmtd-label">' + escapeHtml(lbl("VAS_089_ReceiptsMTD", "Receipts MTD")) + '</div>' +
                '<div class="vas-rmtd-value">—</div>' +
                '<div class="vas-rmtd-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-rmtd-value');
            $metaEl = $card.find('.vas-rmtd-meta');

            $root.append($card);

            $busy = $('<div class="vas-rmtd-busy vas-rmtd-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VAS.VAS_089_ReceiptsMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the label clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_089_ReceiptsMTDWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_089_ReceiptsMTDWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_089_ReceiptsMTDWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
