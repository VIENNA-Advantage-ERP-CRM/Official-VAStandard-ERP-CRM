/**
 * Paid this month
 * Purpose - Shows total outgoing AP payment amount posted in the current month.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Paid this month                      | VAS_PaidThisMonth
 *  2  | WHY                                  | VAS_Why
 *  3  | Outgoing payments posted so far      | VAS_OutgoingPaymentsPostedSoFar
 *  4  | Loading                              | VAS_Loading
 *  5  | No Data                              | VAS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */

; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.PaidThisMonthWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $self = this;
        var $root = $('<div class="vas-paid-this-month-root">');
        var $card;
        var $title;
        var $value;
        var $badge;
        var $description;
        var $body;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-paid-this-month-card">');

            var $header = $('<div class="vas-paid-this-month-header">');
            var $iconBox = $('<div class="vas-paid-this-month-icon-box">');
            var $icon = $(
                '<svg class="vas-paid-this-month-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<line x1="22" y1="2" x2="11" y2="13"></line>' +
                '<polygon points="22 2 15 22 11 13 2 9 22 2"></polygon>' +
                '</svg>'
            );

            $title = $('<div class="vas-paid-this-month-title">').text(lbl('VAS_PaidThisMonth', 'Paid this month'));

            $iconBox.append($icon);
            $header.append($iconBox).append($title);

            $body = $('<div class="vas-paid-this-month-body">');
            $value = $('<div class="vas-paid-this-month-value">');
            $body.append($value);

            var $footer = $('<div class="vas-paid-this-month-footer">');
            $badge = $('<div class="vas-paid-this-month-badge">').text(lbl('VAS_Why', 'WHY'));
            $description = $('<div class="vas-paid-this-month-desc">').text(lbl('VAS_OutgoingPaymentsPostedSoFar', 'Outgoing payments posted so far'));

            $footer.append($badge).append($description);
            $card.append($header).append($body).append($footer);
            $root.empty().append($card);
        }

        function loadData() {
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'PaidThisMonth/GetPaidThisMonth',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    var data = response;

                    if (typeof response === 'string') {
                        try {
                            data = JSON.parse(response);
                        }
                        catch (e) {
                            setNoData();
                            return;
                        }
                    }

                    if (!data || data.error) {
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

        function renderData(data) {
            var amount = Number(data.paidThisMonth);

            if (isNaN(amount)) {
                setNoData();
                return;
            }

            $value.text(formatCurrencyAmount(amount, data.currencySymbol, data.currencyISO));
            $body.removeClass('vas-paid-this-month-state');
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO) {
            var numericValue = Number(value || 0);
            var absValue = Math.abs(numericValue);
            var sign = numericValue < 0 ? '-' : '';
            var symbol = currencySymbol || currencyISO || '';

            if (absValue >= 10000000) {
                return sign + symbol + formatCompactNumber(absValue / 10000000, 2) + 'Cr';
            }

            if (absValue >= 100000) {
                return sign + symbol + formatCompactNumber(absValue / 100000, 2) + 'L';
            }

            if (absValue >= 1000) {
                return sign + symbol + formatCompactNumber(absValue / 1000, 2) + 'K';
            }

            return sign + symbol + absValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: 2
            });
        }

        function formatCompactNumber(value, precision) {
            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: precision
            });
        }

        function setLoading() {
            if ($value) {
                $value.text(lbl('VAS_Loading', 'Loading'));
            }

            if ($body) {
                $body.addClass('vas-paid-this-month-state');
            }
        }

        function setNoData() {
            if ($value) {
                $value.text(lbl('VAS_NoData', 'No Data'));
            }

            if ($body) {
                $body.addClass('vas-paid-this-month-state');
            }
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
            $card = null;
            $title = null;
            $value = null;
            $badge = null;
            $description = null;
            $body = null;
        };
    };

    VIS.PaidThisMonthWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.PaidThisMonthWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.PaidThisMonthWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.PaidThisMonthWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);