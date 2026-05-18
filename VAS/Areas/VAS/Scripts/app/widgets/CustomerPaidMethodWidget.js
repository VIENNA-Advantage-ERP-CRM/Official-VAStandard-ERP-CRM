/**
 * Customer Paid Method Widget
 * Purpose - Show how customers paid through AR receipts.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+--------------------------------
 *  1  | How customers paid                        | VIS_HowCustomersPaid
 *  2  | WHY                                       | VIS_Why
 *  3  | UPI dominates — instant settle, near-zero fees | VIS_CustomerPaidWhy
 *  4  | Loading…                                  | VIS_Loading
 *  5  | No data                                   | VIS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.CustomerPaidMethodWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-cpm-root">');
        var $listBody;
        var $whyText;

        var METHOD_COLORS = [
            "#4F7FEA",
            "#65B69C",
            "#C99135",
            "#7661B8",
            "#E07070",
            "#7A8EA8"
        ];

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
                url: VIS.Application.contextUrl + 'CustomerPaidMethod/GetCustomerPaidMethod',
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

                    if (Array.isArray(data)) {
                        renderRows(data);
                    }
                    else {
                        setNoData();
                    }
                },
                error: function () {
                    setNoData();
                }
            });
        }

        function setLoading() {
            if ($listBody) {
                $listBody.html(
                    '<div class="vas-cpm-nodata">' +
                    lbl("VIS_Loading", "Loading…") +
                    '</div>'
                );
            }
        }

        function setNoData() {
            if ($listBody) {
                $listBody.html(
                    '<div class="vas-cpm-nodata">' +
                    lbl("VIS_NoData", "No data") +
                    '</div>'
                );
            }

            if ($whyText) {
                $whyText.text("");
            }
        }

        function normalizeMethodName(name) {
            if (!name) {
                return "Other";
            }

            var n = name.toString().trim();

            if (n.toUpperCase() === "NEFT" || n.toUpperCase() === "RTGS" || n.toUpperCase() === "NEFT/RTGS") {
                return "NEFT / RTGS";
            }

            return n;
        }

        function renderRows(rows) {
            if (!$listBody) {
                return;
            }

            $listBody.empty();

            if (!rows || rows.length === 0) {
                setNoData();
                return;
            }

            var maxRows = Math.min(rows.length, 4);
            var topMethodName = "";

            for (var i = 0; i < maxRows; i++) {
                var row = rows[i];
                var methodName = normalizeMethodName(row.paymentMethodName);
                var percent = Number(row.paymentMethodPercent || 0);

                if (i === 0) {
                    topMethodName = methodName;
                }

                var color = METHOD_COLORS[i % METHOD_COLORS.length];

                var $row = $(
                    '<div class="vas-cpm-row">' +
                    '<div class="vas-cpm-row-head">' +
                    '<span class="vas-cpm-method">' + methodName + '</span>' +
                    '<span class="vas-cpm-percent">' + percent.toFixed(0) + '%</span>' +
                    '</div>' +
                    '<div class="vas-cpm-track">' +
                    '<div class="vas-cpm-fill" style="width:' + percent + '%;background:' + color + ';"></div>' +
                    '</div>' +
                    '</div>'
                );

                $listBody.append($row);
            }

            if ($whyText) {
                if (topMethodName) {
                    $whyText.text(topMethodName + " dominates — instant settle, near-zero fees");
                }
                else {
                    $whyText.text(lbl("VIS_CustomerPaidWhy", "Payment methods distribution"));
                }
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-cpm-card">' +
                '<div class="vas-cpm-header">' +
                '<div class="vas-cpm-icon-wrap">' +
                '<svg width="25" height="25" viewBox="0 0 24 24" fill="none" ' +
                'stroke="#4F7FEA" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M21.21 15.89A10 10 0 1 1 8 2.83"/>' +
                '<path d="M22 12A10 10 0 0 0 12 2v10z"/>' +
                '</svg>' +
                '</div>' +
                '<div class="vas-cpm-title">' +
                lbl("VIS_HowCustomersPaid", "How customers paid") +
                '</div>' +
                '</div>' +

                '<div class="vas-cpm-list">' +
                '<div class="vas-cpm-nodata">' +
                lbl("VIS_Loading", "Loading…") +
                '</div>' +
                '</div>' +

                '<div class="vas-cpm-divider"></div>' +

                '<div class="vas-cpm-footer">' +
                '<span class="vas-cpm-why">' +
                lbl("VIS_Why", "WHY") +
                '</span>' +
                '<span class="vas-cpm-desc"></span>' +
                '</div>' +
                '</div>'
            );

            $listBody = $card.find('.vas-cpm-list');
            $whyText = $card.find('.vas-cpm-desc');

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

    VIS.CustomerPaidMethodWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.CustomerPaidMethodWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.CustomerPaidMethodWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.CustomerPaidMethodWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);