/**
 * Expected This Week Widget
 * Purpose - Show AR invoice amount due in the next 7 days.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                    | Message Key
 * ----+---------------------------------+--------------------------------
 *  1  | Expected this week              | VIS_ExpectedThisWeek
 *  2  | Invoices due in next 7 days     | VIS_InvoicesDueNext7Days
 *  3  | WHY                             | VIS_Why
 *  4  | Loading…                        | VIS_Loading
 *  5  | No data                         | VIS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.ExpectedThisWeekWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-etw-root">');
        var $amountText;
        var $whyDesc;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function loadData() {
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'ExpectedThisWeek/GetExpectedThisWeek',
                type: 'GET',
                success: function (res) {
                    var data = res;

                    if (typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    if (typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    if (data && data.error) {
                        setNoData();
                        return;
                    }

                    renderData(data);
                },
                error: function () {
                    setNoData();
                }
            });
        }

        function setLoading() {
            if ($amountText) {
                $amountText.text("...");
            }

            if ($whyDesc) {
                $whyDesc.text(lbl("VIS_Loading", "Loading…"));
            }
        }

        function setNoData() {
            if ($amountText) {
                $amountText.text("0.00");
            }

            if ($whyDesc) {
                $whyDesc.text(lbl("VIS_NoData", "No data"));
            }
        }

        function renderData(data) {
            var amount = Number(data && data.expectedAmountThisWeek || 0);

            if ($amountText) {
                $amountText.text(formatCompactAmount(amount));
            }

            if ($whyDesc) {
                $whyDesc.text(lbl("VIS_InvoicesDueNext7Days", "Invoices due in next 7 days"));
            }
        }

        function formatCompactAmount(value) {
            value = Number(value || 0);

            if (value >= 10000000) {
                return (value / 10000000).toFixed(2).replace(/\.00$/, "") + "Cr";
            }

            if (value >= 100000) {
                return (value / 100000).toFixed(2).replace(/\.00$/, "") + "L";
            }

            if (value >= 1000) {
                return (value / 1000).toFixed(2).replace(/\.00$/, "") + "K";
            }

            var absVal = value;
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            return absVal.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        function createWidget() {
            var $card = $(
                '<div class="widget kpi tint-success span-2x1">' +

                '<div class="kpi-head">' +
                '<div class="kpi-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect>' +
                '<line x1="16" y1="2" x2="16" y2="6"></line>' +
                '<line x1="8" y1="2" x2="8" y2="6"></line>' +
                '<line x1="3" y1="10" x2="21" y2="10"></line>' +
                '</svg>' +
                '</div>' +
                '<span class="kpi-label">' +
                lbl("VIS_ExpectedThisWeek", "Expected this week") +
                '</span>' +
                '</div>' +

                '<span class="kpi-value">...</span>' +

                '<div class="kpi-why">' +
                '<span class="kpi-why-tag">' +
                lbl("VIS_Why", "WHY") +
                '</span>' +
                '<span  class="kpi-why-text-tag">' +
                lbl("VIS_InvoicesDueNext7Days", "Invoices due in next 7 days") +
                '</span>' +
                '</div>' +

                '</div>'
            );

            $amountText = $card.find('.kpi-value');
            $whyDesc = $card.find('.kpi-why span:last-child');

            $root.append($card);
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.ExpectedThisWeekWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;

        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.ExpectedThisWeekWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.ExpectedThisWeekWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.ExpectedThisWeekWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);