/**
 * Total Amount Received This Month Widget
 * Purpose - Show total AR receipt amount received this month.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+-------------------------------
 *  1  | Received this month                  | VIS_ReceivedThisMonth
 *  2  | Customer collections posted so far   | VIS_CustomerCollectionsPosted
 *  3  | WHY                                  | VIS_Why
 *  4  | Loading…                             | VIS_Loading
 *  5  | No data                              | VIS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.TotalAmountReceivedThisMonthWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-tarm-root">');
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
                url: VIS.Application.contextUrl + 'TotalAmountReceivedThisMonth/GetAmountReceivedThisMonth',
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
                amount = data.totalAmountReceivedThisMonth || data.totalAmountReceivedJanuary || 0;
            }

            if ($amountText) {
                $amountText.text(formatCompactAmount(amount));
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-tarm-card">' +
                '<div class="vas-tarm-header">' +
                '<div class="vas-tarm-icon-wrap">' +
                '<svg width="25" height="25" viewBox="0 0 24 24" fill="none" ' +
                'stroke="#3F66AE" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M21 8V7a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v1"/>' +
                '<path d="M3 8h18l-2 9H5L3 8z"/>' +
                '<path d="M8 12h8"/>' +
                '</svg>' +
                '</div>' +
                '<div class="vas-tarm-title">' +
                lbl("VIS_ReceivedThisMonth", "Received this month") +
                '</div>' +
                '</div>' +

                '<div class="vas-tarm-amount-row">' +
                '<span class="vas-tarm-amount">' +
                lbl("VIS_Loading", "Loading…") +
                '</span>' +
                '</div>' +

                '<div class="vas-tarm-footer">' +
                '<span class="vas-tarm-why">' +
                lbl("VIS_Why", "WHY") +
                '</span>' +
                '<span class="vas-tarm-desc">' +
                lbl("VIS_CustomerCollectionsPosted", "Customer collections posted so far") +
                '</span>' +
                '</div>' +
                '</div>'
            );

            $amountText = $card.find('.vas-tarm-amount');
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

    VIS.TotalAmountReceivedThisMonthWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.TotalAmountReceivedThisMonthWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.TotalAmountReceivedThisMonthWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.TotalAmountReceivedThisMonthWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);