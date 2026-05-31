/**
 * Scheduled
 * Purpose - Displays AP invoice outstanding due amounts scheduled for payment during the current week, grouped by payment method.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Scheduled                            | VAS_Scheduled
 *  2  | WHY                                  | VAS_Why
 *  3  | Queued for {0} run this week         | VAS_QueuedForPaymentMethodRunThisWeek
 *  4  | Scheduled for payment this week      | VAS_ScheduledForPaymentThisWeek
 *  5  | Loading                              | VAS_Loading
 *  6  | No Data                              | VAS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */

; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.ScheduledAPPaymentWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-scheduled-ap-payment-root">');
        var $card;
        var $title;
        var $value;
        var $badge;
        var $description;
        var $body;
        var groups = [];
        var groupIndex = 0;
        var rotationTimer = null;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-scheduled-ap-payment-card">');

            var $header = $('<div class="vas-scheduled-ap-payment-header">');
            var $iconBox = $('<div class="vas-scheduled-ap-payment-icon-box">');
            var $icon = $(
                '<svg class="vas-scheduled-ap-payment-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<circle cx="12" cy="12" r="10"></circle>' +
                '<polyline points="12 6 12 12 16 14"></polyline>' +
                '</svg>'
            );

            $title = $('<div class="vas-scheduled-ap-payment-title">').text(lbl('VAS_Scheduled', 'Scheduled'));

            $iconBox.append($icon);
            $header.append($iconBox).append($title);

            $body = $('<div class="vas-scheduled-ap-payment-body">');
            $value = $('<div class="vas-scheduled-ap-payment-value">');
            $body.append($value);

            var $footer = $('<div class="vas-scheduled-ap-payment-footer">');
            $badge = $('<div class="vas-scheduled-ap-payment-badge">').text(lbl('VAS_Why', 'WHY'));
            $description = $('<div class="vas-scheduled-ap-payment-desc">');

            $footer.append($badge).append($description);
            $card.append($header).append($body).append($footer);
            $root.empty().append($card);
        }

        function loadData() {
            stopRotation();
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'ScheduledAPPayment/GetScheduledAPPaymentThisWeek',
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
            groups = $.isArray(data.groups) ? data.groups : [];
            groupIndex = 0;

            if (groups.length > 0) {
                renderGroup(groups[groupIndex], data);
                startRotation(data);
                return;
            }

            var totalAmount = Number(data.scheduledAmountThisWeek);

            if (isNaN(totalAmount) || totalAmount <= 0) {
                setNoData();
                return;
            }

            $value.text(formatCurrencyAmount(totalAmount, data.currencySymbol, data.currencyISO));
            $description.text(lbl('VAS_ScheduledForPaymentThisWeek', 'Scheduled for payment this week'));
            $body.removeClass('vas-scheduled-ap-payment-state');
        }

        function renderGroup(group, data) {
            var amount = Number(group.scheduledAmount);

            if (isNaN(amount)) {
                setNoData();
                return;
            }

            var paymentMethodName = group.paymentMethodName || lbl('VAS_NotSpecified', 'Not Specified');
            var footerText = lbl('VAS_QueuedForPaymentMethodRunThisWeek', 'Queued for {0} run this week').replace('{0}', paymentMethodName);

            $value.text(formatCurrencyAmount(amount, group.currencySymbol || data.currencySymbol, group.currencyISO || data.currencyISO));
            $description.text(footerText);
            $body.removeClass('vas-scheduled-ap-payment-state');
        }

        function startRotation(data) {
            stopRotation();

            if (groups.length <= 1) {
                return;
            }

            rotationTimer = window.setInterval(function () {
                groupIndex = groupIndex + 1;

                if (groupIndex >= groups.length) {
                    groupIndex = 0;
                }

                renderGroup(groups[groupIndex], data);
            }, 5000);
        }

        function stopRotation() {
            if (rotationTimer) {
                window.clearInterval(rotationTimer);
                rotationTimer = null;
            }
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO) {
            var numericValue = Number(value || 0);
            var absValue = Math.abs(numericValue);
            var sign = numericValue < 0 ? '-' : '';

            if (absValue >= 10000000) {
                return sign + formatCompactNumber(absValue / 10000000) + 'Cr';
            }

            if (absValue >= 100000) {
                return sign + formatCompactNumber(absValue / 100000) + 'L';
            }

            if (absValue >= 1000) {
                return sign + formatCompactNumber(absValue / 1000) + 'K';
            }

            return sign + absValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: getStdPrecision(),
                maximumFractionDigits: getStdPrecision()
            });
        }

        function formatCompactNumber(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: getStdPrecision(),
                maximumFractionDigits: getStdPrecision()
            });
        }

        function getStdPrecision() {
            var stdPrecision = 2;

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = 2;
            }

            return stdPrecision;
        }

        function setLoading() {
            if ($value) {
                $value.text(lbl('VAS_Loading', 'Loading'));
            }

            if ($description) {
                $description.text('');
            }

            if ($body) {
                $body.addClass('vas-scheduled-ap-payment-state');
            }
        }

        function setNoData() {
            if ($value) {
                $value.text('0');
            }

            if ($description) {
                $description.text(lbl('VAS_ScheduledForPaymentThisWeek', 'Scheduled for payment this week'));
            }

            if ($body) {
                $body.addClass('vas-scheduled-ap-payment-state');
            }
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            stopRotation();
            $root.remove();
            $card = null;
            $title = null;
            $value = null;
            $badge = null;
            $description = null;
            $body = null;
            groups = [];
        };
    };

    VIS.ScheduledAPPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.ScheduledAPPaymentWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.ScheduledAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.ScheduledAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);