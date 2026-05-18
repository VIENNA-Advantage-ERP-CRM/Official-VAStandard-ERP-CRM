/**
 * Avg Days To Pay Widget
 * Purpose - KPI card showing the weighted-average number of days customers take to pay,
 *           compared to the previous quarter, on the home/finance dashboard.
 * Design  - Matches the dark KPI variant shown in the UI mockup: dark navy surface,
 *           large bold metric with a muted 'd' suffix, and a subtitle comparison line.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                  | Message Key                    | MsgText
 * ----+-----------------------------------------------+--------------------------------+-----------------------------------------------
 *  1  | Avg days to pay                               | VIS_AvgDaysToPay               | Avg days to pay
 *  2  | Loading…                                      | VIS_Loading                    | Loading…
 *  3  | No change                                     | VIS_NoChange                   | No change
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.AvgDaysToPayWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-adtp-root">');

        var $metricEl;
        var $subtitleEl;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            loadData();
        };

        /* ── Load data from backend ── */
        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'AvgDaysToPay/GetAvgDaysToPay',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        renderMetric(data.currentAvgDays, data.displayText);
                    }
                },
                error: function () { /* leave placeholder on error */ }
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

                    /* Target/bullseye icon well — matches the SVG in the HTML mockup */
                    '<div class="vas-adtp-icon">' +
                        '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" ' +
                            'stroke="#9AA3B5" stroke-width="1.6" ' +
                            'stroke-linecap="round" stroke-linejoin="round">' +
                            '<circle cx="12" cy="12" r="10"/>' +
                            '<circle cx="12" cy="12" r="6"/>' +
                            '<circle cx="12" cy="12" r="2"/>' +
                        '</svg>' +
                    '</div>' +

                    '<div class="vas-adtp-title">' +
                        lbl('VIS_AvgDaysToPay', 'Avg days to pay') +
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
    };

    VIS.AvgDaysToPayWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.AvgDaysToPayWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
