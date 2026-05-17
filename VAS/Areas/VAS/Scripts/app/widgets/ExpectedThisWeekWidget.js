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

                    renderAmount(data);
                },
                error: function () {
                    setNoData();
                }
            });
        }

        function setLoading() {
            if ($amountText) {
                $amountText.text(lbl("VIS_Loading", "Loading…"));
            }
        }

        function setNoData() {
            if ($amountText) {
                $amountText.text("0.00");
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

            return value.toLocaleString(window.navigator.language, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
        }

        function renderAmount(data) {
            var amount = 0;

            if (data) {
                amount = data.expectedAmountThisWeek || 0;
            }

            if ($amountText) {
                $amountText.text(formatCompactAmount(amount));
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-etw-card">' +
                '<div class="vas-etw-header">' +
                '<div class="vas-etw-icon-wrap">' +
                '<svg width="25" height="25" viewBox="0 0 24 24" fill="none" ' +
                'stroke="#34724B" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>' +
                '<line x1="16" y1="2" x2="16" y2="6"/>' +
                '<line x1="8" y1="2" x2="8" y2="6"/>' +
                '<line x1="3" y1="10" x2="21" y2="10"/>' +
                '</svg>' +
                '</div>' +
                '<div class="vas-etw-title">' +
                lbl("VIS_ExpectedThisWeek", "Expected this week") +
                '</div>' +
                '</div>' +

                '<div class="vas-etw-amount-row">' +
                '<span class="vas-etw-amount">' +
                lbl("VIS_Loading", "Loading…") +
                '</span>' +
                '</div>' +

                '<div class="vas-etw-footer">' +
                '<span class="vas-etw-why">' +
                lbl("VIS_Why", "WHY") +
                '</span>' +
                '<span class="vas-etw-desc">' +
                lbl("VIS_InvoicesDueNext7Days", "Invoices due in next 7 days") +
                '</span>' +
                '</div>' +
                '</div>'
            );

            $amountText = $card.find('.vas-etw-amount');
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