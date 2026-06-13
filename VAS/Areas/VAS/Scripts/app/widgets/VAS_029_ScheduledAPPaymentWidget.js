/**
 * Scheduled
 * Purpose - Displays AP invoice outstanding due amounts scheduled for payment during the current week, grouped by payment method.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Scheduled                            | VAS_029_MessageScheduled
 *  2  | Queued for {0} run this week         | VAS_029_MessageQueuedForPaymentMethodRunThisWeek
 *  3  | Scheduled for payment this week      | VAS_029_MessageScheduledForPaymentThisWeek
 *  4  | Loading                              | VAS_029_MessageLoading
 *  5  | No Data                              | VAS_029_MessageNoData
 * ─────────────────────────────────────────────────────────────────────
 */


; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_029_ScheduledAPPaymentWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $root = $('<div class="vas-scheduled-ap-payment-root">');
        var $card = null;
        var $value = null;
        var $description = null;
        var $body = null;
        var $footer = null;
        var $busy = null;
        var $state = null;

        var groups = [];
        var groupIndex = 0;
        var rotationTimer = null;
        var isDisposed = false;

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
                '<svg class="vas-scheduled-ap-payment-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<circle cx="12" cy="12" r="10"></circle>' +
                '<polyline points="12 6 12 12 16 14"></polyline>' +
                '</svg>'
            );

            var $title = $('<div class="vas-scheduled-ap-payment-title">').text(
                lbl('VAS_029_MessageScheduled', 'Scheduled')
            );

            $iconBox.append($icon);
            $header.append($iconBox).append($title);

            $body = $('<div class="vas-scheduled-ap-payment-body">');
            $value = $('<div class="vas-scheduled-ap-payment-value">');
            $body.append($value);

            $footer = $('<div class="vas-scheduled-ap-payment-footer">');
         

            $description = $('<div class="vas-scheduled-ap-payment-desc">');

            $footer.append($description);
            $busy = $('<div class="vas-scheduled-ap-payment-busy">').text(lbl('VAS_029_MessageLoading', 'Loading'));
            $state = $('<div class="vas-scheduled-ap-payment-state-message">');

            $card.append($header).append($body).append($footer).append($busy).append($state);
            $root.empty().append($card);
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            stopRotation();
            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_029_ScheduledAPPaymentWidget/GetScheduledAPPaymentThisWeek',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = normalizeResponse(response);

                    if (!data || data.error) {
                        showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                        return;
                    }

                    renderData(data);
                },
                error: function () {
                    if (!isDisposed) {
                        showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                    }
                },
                complete: function () {
                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        function normalizeResponse(response) {
            if (typeof response !== 'string') {
                return response;
            }

            try {
                return JSON.parse(response);
            }
            catch (e) {
                return null;
            }
        }

        function renderData(data) {
            groups = $.isArray(data.groups)
                ? $.grep(data.groups, function (group) {
                    var amount = Number(group.value);

                    if (isNaN(amount)) {
                        amount = Number(group.scheduledAmount);
                    }

                    return !isNaN(amount) && amount > 0;
                })
                : [];
            groupIndex = 0;

            if (groups.length > 0) {
                renderGroup(groups[groupIndex], data);
                startRotation(data);
                return;
            }

            var totalAmount = Number(data.value);

            if (isNaN(totalAmount)) {
                totalAmount = Number(data.scheduledAmountThisWeek);
            }

            if (isNaN(totalAmount) || totalAmount <= 0) {
                setNoData();
                return;
            }

            showState(false, '');

            $value.text(formatCurrencyAmount(
                totalAmount,
                data.currencySymbol || data.symbol,
                data.currencyISO,
                data.precision
            ));

            $description.text(
                data.description || lbl('VAS_029_MessageScheduledForPaymentThisWeek', 'Scheduled for payment this week')
            );
        }

        function renderGroup(group, data) {
            if (!group) {
                setNoData();
                return;
            }

            var amount = Number(group.value);

            if (isNaN(amount)) {
                amount = Number(group.scheduledAmount);
            }

            if (isNaN(amount) || amount <= 0) {
                setNoData();
                return;
            }

            showState(false, '');

            var precision = normalizePrecision(group.precision || data.precision);
            var paymentMethodName = group.paymentMethodName || lbl('VAS_029_MessageNotSpecified', 'Not Specified');

            var footerText = lbl(
                'VAS_029_MessageQueuedForPaymentMethodRunThisWeek',
                'Queued for {0} run this week'
            ).replace('{0}', paymentMethodName);

            $value.text(formatCurrencyAmount(
                amount,
                group.currencySymbol || data.currencySymbol || data.symbol,
                group.currencyISO || data.currencyISO,
                precision
            ));

            $description.text(footerText);
        }

        function startRotation(data) {
            stopRotation();

            if (groups.length <= 1) {
                return;
            }

            rotationTimer = window.setInterval(function () {
                if (isDisposed) {
                    stopRotation();
                    return;
                }

                groupIndex += 1;

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

        function formatCurrencyAmount(value, currencySymbol, currencyISO, precision) {
            var numericValue = Number(value || 0);
            var sign = numericValue < 0 ? '-' : '';
            var absValue = Math.abs(numericValue);
            var amount = formatAmount(absValue, normalizePrecision(precision));

            if (currencySymbol) {
                return sign + currencySymbol + amount;
            }

            return currencyISO ? sign + amount + ' ' + currencyISO : sign + amount;
        }

        function formatAmount(value, precision) {
            var numericValue = Number(value || 0);

            if (numericValue >= 10000000) {
                return (numericValue / 10000000).toFixed(2).replace(/\.00$/, '') + 'Cr';
            }

            if (numericValue >= 100000) {
                return (numericValue / 100000).toFixed(2).replace(/\.00$/, '') + 'L';
            }

            if (numericValue >= 1000) {
                return Math.round(numericValue / 1000) + 'k';
            }

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
        }

        function normalizePrecision(precision) {
            var stdPrecision = Number(precision);

            if (!isNaN(stdPrecision) && stdPrecision >= 0) {
                return stdPrecision;
            }

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                return 2;
            }

            return stdPrecision;
        }

        function showBusy(show) {
            if ($busy) {
                $busy.toggleClass('is-visible', !!show);
            }
        }

        function showState(show, message) {
            if ($state) {
                $state.text(message || '').toggleClass('is-visible', !!show);
            }

            if ($body) {
                $body.toggle(!show);
            }

            if ($footer) {
                $footer.toggle(!show);
            }
        }

        function setNoData() {
            if ($description) {
                $description.text('');
            }

            showState(true, lbl('VAS_029_MessageNoData', 'No Data'));
        }

        this.refreshData = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;
            stopRotation();

            $root.remove();

            $card = null;
            $value = null;
            $description = null;
            $body = null;
            $footer = null;
            $busy = null;
            $state = null;
            groups = [];
        };
    };

    VAS.VAS_029_ScheduledAPPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;

        if (frame && frame.widgetInfo) {
            this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        }

        this.Initalize();

        if (this.frame && this.frame.getContentGrid) {
            this.frame.getContentGrid().append(this.getRoot());
        }
    };

    VAS.VAS_029_ScheduledAPPaymentWidget.prototype.widgetSizeChange = function (height, width) {
        var $root = this.getRoot();

        if (!$root) {
            return;
        }

        $root.toggleClass(
            'vas-scheduled-ap-payment-compact',
            (width && width < 240) || (height && height < 160)
        );
    };

    VAS.VAS_029_ScheduledAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_029_ScheduledAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame && this.frame.dispose) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
