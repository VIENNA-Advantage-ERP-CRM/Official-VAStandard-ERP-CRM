/**
 * Counted MTD Widget (Physical Inventory / Inventory Count Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing month-to-date completed count lines
 *           and distinct products counted.
 * Prefix  - VAS_156_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Counted MTD                                      | VAS_156_CountedMTD
 *  2  | No products counted yet this month               | VAS_156_NoProductsCounted
 *  3  | product counted this month                       | VAS_156_ProductCountedMonth
 *  4  | products counted this month                      | VAS_156_ProductsCountedMonth
 *  5  | Unable to load count data                        | VAS_156_UnableToLoadData
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_156_CountedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-counted-mtd-container">');
        var $root = $('<div class="vas-counted-mtd-root">');
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
            $busy.toggleClass('vas-counted-mtd-hidden', !show);
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
                url: VIS.Application.contextUrl + 'VAS_156_CountedMTDWidget/GetCountedMTDData',
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
            var lines = Number(data.countedLines || 0);
            var products = Number(data.productsCounted || 0);

            if ($valueEl) {
                $valueEl.text(formatCount(lines));
                $valueEl.attr('title', formatCount(lines));
            }

            if ($metaEl) {
                var metaMsg = "";
                if (products === 0) {
                    metaMsg = lbl("VAS_156_NoProductsCounted", "No products counted yet this month");
                } else if (products === 1) {
                    metaMsg = "1 " + lbl("VAS_156_ProductCountedMonth", "product counted this month");
                } else {
                    metaMsg = formatCount(products) + " " + lbl("VAS_156_ProductsCountedMonth", "products counted this month");
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
                var errText = lbl("VAS_156_UnableToLoadData", "Unable to load count data");
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-counted-mtd-card">' +
                '<div class="vas-counted-mtd-label">' + escapeHtml(lbl("VAS_156_CountedMTD", "Counted MTD")) + '</div>' +
                '<div class="vas-counted-mtd-value">—</div>' +
                '<div class="vas-counted-mtd-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-counted-mtd-value');
            $metaEl = $card.find('.vas-counted-mtd-meta');

            $root.append($card);

            $busy = $('<div class="vas-counted-mtd-busy vas-counted-mtd-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VAS.VAS_156_CountedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_156_CountedMTDWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_156_CountedMTDWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_156_CountedMTDWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);

