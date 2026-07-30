/**
 * Variance Lines KPI Widget (Inventory Count Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing month-to-date count lines
 *           whose difference quantity exceeds accepted product tolerance.
 * Prefix  - VAS_157_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Variance Lines                                   | VAS_157_VarianceLines
 *  2  | No count line variances detected this month      | VAS_157_NoVariancesDetected
 *  3  | count line requires adjustment review            | VAS_157_VarianceLineRequiresReview
 *  4  | count lines require adjustment review            | VAS_157_VarianceLinesRequireReview
 *  5  | Unable to load variance data                     | VAS_157_UnableToLoadVarianceData
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_157_VarianceLinesWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-variance-lines-container">');
        var $root = $('<div class="vas-variance-lines-root">');
        var $valueEl;
        var $metaEl;
        var $busy;
        var widgetObserver = null;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-variance-lines-hidden', !show);
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

        function setupWidgetSizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            widgetObserver = new ResizeObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) {
                        $root[0].style.setProperty('--widget-inline-size', width + 'px');
                    }
                }
            });
            widgetObserver.observe($wrapper[0]);
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_157_VarianceLinesWidget/GetVarianceLinesData',
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
            var varianceLines = Number(data.varianceLines || 0);
            var countedLines = Number(data.countedLines || 0);

            if ($valueEl) {
                $valueEl.text(formatCount(varianceLines));
                $valueEl.attr('title', formatCount(varianceLines));
            }

            if ($metaEl) {
                var metaMsg = "";
                if (countedLines === 0) {
                    metaMsg = lbl("VAS_157_NoLinesCountedMonth", "No lines counted this month");
                } else {
                    var pct = ((varianceLines / countedLines) * 100).toFixed(1);
                    var ofText = lbl("VAS_157_OfCountedLines", "of counted lines");
                    metaMsg = pct + "% " + ofText;
                }
                $metaEl.text(metaMsg);
                $metaEl.attr('title', metaMsg);
            }
        }

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) {
                var errText = lbl("VAS_157_UnableToLoadVariance", "Unable to load variance data");
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-variance-lines-card">' +
                '<div class="vas-variance-lines-label">' + escapeHtml(lbl("VAS_157_VarianceLines", "Variance Lines")) + '</div>' +
                '<div class="vas-variance-lines-value">—</div>' +
                '<div class="vas-variance-lines-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-variance-lines-value');
            $metaEl = $card.find('.vas-variance-lines-meta');

            $root.append($card);

            $busy = $('<div class="vas-variance-lines-busy vas-variance-lines-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $wrapper.append($root);
            setupWidgetSizeObserver();
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            $wrapper.remove();
        };
    };

    VAS.VAS_157_VarianceLinesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_157_VarianceLinesWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_157_VarianceLinesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_157_VarianceLinesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
