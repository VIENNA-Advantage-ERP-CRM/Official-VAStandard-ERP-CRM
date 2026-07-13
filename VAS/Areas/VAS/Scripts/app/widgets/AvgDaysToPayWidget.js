/**
 * Avg Days To Pay Widget
 * Purpose - KPI card showing the weighted-average number of days customers take to pay,
 *           compared to the previous quarter, on the home/finance dashboard.
 * Design  - Onfinity glass KPI card (design.md §8): light glass surface, tinted icon
 *           well, large bold metric with a muted 'd' suffix, and a subtitle comparison
 *           line. Sizing is em-based so the card scales as a whole inside its grid cell.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                  | Message Key                    | MsgText
 * ----+-----------------------------------------------+--------------------------------+-----------------------------------------------
 *  1  | Avg Days to Pay                               | VAS_056_AvgDaysToPay           | Avg days to pay
 *  2  | Loading…                                      | VIS_Loading                    | Loading…
 *  3  | No change                                     | VIS_NoChange                   | No change
 *  4  | Days to Pay (This Quarter)                    | VAS_056_DaysToPayThisQuarter   | Days to Pay (This Quarter)
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

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

    VIS.AvgDaysToPayWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-adtp-root">');

        var $metricEl;
        var $subtitleEl;
        /* Busy/loading overlay shown while data is being fetched (initial load + refresh). */
        var $busy;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        /* Toggle the busy/loading overlay. */
        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            loadData();
        };

        /* ── Load data from backend ── */
        function loadData() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'AvgDaysToPay/GetAvgDaysToPay',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        renderMetric(data.currentAvgDays, data.displayText);
                    }
                },
                error: function () { /* leave placeholder on error */ },
                complete: function () { showBusy(false); }
            });
        }

        /* ── Render metric values ── */
        function renderMetric(days, displayText) {
            if ($metricEl) {
                $metricEl.html(
                    '<span class="vas-adtp-metric-val">' +
                        (days || 0) +
                    '</span>' +
                    '<span class="vas-adtp-metric-suffix">d</span>'
                );
            }
            if ($subtitleEl) {
                $subtitleEl.text(displayText || lbl('VIS_NoChange', 'No change'));
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var uid = $self.AD_UserHomeWidgetID || 'adtp';

            /* Dark navy card surface to match the UI mockup */
            var $card = $(
                '<div class="vas-adtp-card">'
            );

            /* ── Header row: icon + label ── */
            var $header = $(
                '<div class="vas-adtp-header">' +

                    /* Target/bullseye icon well — colour comes from CSS (currentColor) */
                    '<div class="vas-adtp-icon">' +
                        '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" ' +
                            'stroke="currentColor" stroke-width="1.6" ' +
                            'stroke-linecap="round" stroke-linejoin="round">' +
                            '<circle cx="12" cy="12" r="10"/>' +
                            '<circle cx="12" cy="12" r="6"/>' +
                            '<circle cx="12" cy="12" r="2"/>' +
                        '</svg>' +
                    '</div>' +

                    '<div class="vas-adtp-labels">' +
                        '<div class="vas-adtp-title">' +
                            lbl('VAS_056_AvgDaysToPay', 'Avg Days to Pay') +
                        '</div>' +
                        '<div class="vas-adtp-header-subtitle">' +
                            lbl('VAS_056_DaysToPayThisQuarter', 'Days to Pay (This Quarter)') +
                        '</div>' +
                    '</div>' +

                '</div>'
            );

            /* ── Metric: large number + 'd' suffix ── */
            $metricEl = $(
                '<div id="vis-adtp-metric-' + uid + '" class="vas-adtp-metric-wrap">' +
                    '<span class="vas-adtp-metric-val">—</span>' +
                '</div>'
            );

            /* ── Subtitle: comparison vs last quarter ── */
            $subtitleEl = $(
                '<div id="vis-adtp-subtitle-' + uid + '" class="vas-adtp-subtitle">' +
                    lbl('VIS_Loading', 'Loading…') +
                '</div>'
            );

            $card.append($header).append($metricEl).append($subtitleEl);
            $root.append($card);

            /* Busy/loading overlay over the whole card, using the core spinner classes. Hidden until
               a fetch is in flight; shown for both initial load and refresh. */
            $busy = $('<div class="vas-adtp-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            if ($metricEl) {
                $metricEl.html(
                    '<span class="vas-adtp-metric-val">—</span>'
                );
            }
            if ($subtitleEl) {
                $subtitleEl.text(lbl('VIS_Loading', 'Loading…'));
            }
            loadData();
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.AvgDaysToPayWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.AvgDaysToPayWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VIS.AvgDaysToPayWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.AvgDaysToPayWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
