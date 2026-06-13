/**
 * Payment methods
 * Purpose - Shows the distribution of outgoing AP payments by payment method.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Payment methods                      | VAS_033_MessagePaymentMethods
 *  2  | UPI is cheapest - shift small payments where possible | VAS_033_MessagePaymentMethodWhy
 *  3  | Loading                              | VAS_033_MessageLoading
 *  4  | No Data                              | VAS_033_MessageNoData
 *  5  | Not Specified                        | VAS_033_MessageNotSpecified
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_033_PaymentMethodsWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-payment-methods-root">');
        var $card;
        var $body;
        var $foot;
        var $busy;
        var $state;
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
            $card = $('<div class="vas-payment-methods-card">');

            var $head = $('<div class="vas-payment-methods-head">');
            var $iconBox = $('<span class="vas-payment-methods-icon-box">');
            var $icon = $(
                '<svg class="vas-payment-methods-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<path d="M21.21 15.89A10 10 0 1 1 8 2.83"></path>' +
                '<path d="M22 12A10 10 0 0 0 12 2v10z"></path>' +
                '</svg>'
            );
            var $title = $('<div class="vas-payment-methods-title">').text(lbl('VAS_033_MessagePaymentMethods', 'Payment methods'));

            $iconBox.append($icon);
            $head.append($iconBox).append($title);

            $body = $('<div class="vas-payment-methods-body">');

            $foot = $('<div class="vas-payment-methods-foot">');
          
            var $whyText = $('<span class="vas-payment-methods-foot-text">').text(lbl('VAS_033_MessagePaymentMethodWhy', 'UPI is cheapest - shift small payments where possible'));

            $foot.append($whyText);
            $busy = $('<div class="vas-payment-methods-busy">').text(lbl('VAS_033_MessageLoading', 'Loading'));
            $state = $('<div class="vas-payment-methods-state-message">');

            $card.append($head).append($body).append($foot).append($busy).append($state);
            $root.empty().append($card);
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_033_PaymentMethodsWidget/GetPaymentMethods',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = response;

                    if (typeof response === 'string') {
                        try {
                            data = JSON.parse(response);
                        }
                        catch (e) {
                            showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                            return;
                        }
                    }

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

        function renderData(data) {
            var methods = $.isArray(data.methods)
                ? $.grep(data.methods, function (method) {
                    return method != null;
                })
                : [];

            if (methods.length === 0) {
                setNoData();
                return;
            }

            showState(false, '');
            $body.empty();
            $foot.show();

            for (var i = 0; i < methods.length; i++) {
                $body.append(createMethodRow(methods[i]));
            }
        }


        function createMethodRow(method) {
            var methodName = method.paymentMethodName || lbl('VAS_033_MessageNotSpecified', 'Not Specified');
            var percentage = Number(method.percentage || 0);

            if (isNaN(percentage)) {
                percentage = 0;
            }

            if (percentage > 100) {
                percentage = 100;
            }

            var $row = $('<div class="vas-payment-methods-row">');
            var $top = $('<div class="vas-payment-methods-row-top">');
            var $name = $('<span class="vas-payment-methods-name">').text(methodName);
            var $metrics = $('<span class="vas-payment-methods-metrics">');
            var $percent = $('<span class="vas-payment-methods-percent">').text(formatPercentage(percentage));
            var $amount = $('<span class="vas-payment-methods-amount">').text(formatCurrencyAmount(
                method.paymentAmount,
                method.currencySymbol || method.symbol,
                method.currencyISO,
                method.stdPrecision
            ));
            var $track = $('<div class="vas-payment-methods-track">');
            var $fill = $('<div class="vas-payment-methods-fill">').addClass(getMethodClass(methodName));

            $fill.css('width', percentage + '%');

            $metrics.append($percent).append($amount);
            $top.append($name).append($metrics);
            $track.append($fill);
            $row.append($top).append($track);

            return $row;
        }

        function getMethodClass(methodName) {
            var name = (methodName || '').toLowerCase();

            if (name.indexOf('rtgs') >= 0) {
                return 'vas-payment-methods-fill-rtgs';
            }

            if (name.indexOf('upi') >= 0) {
                return 'vas-payment-methods-fill-upi';
            }

            if (name.indexOf('card') >= 0) {
                return 'vas-payment-methods-fill-card';
            }

            if (name.indexOf('cheque') >= 0 || name.indexOf('check') >= 0) {
                return 'vas-payment-methods-fill-cheque';
            }

            return 'vas-payment-methods-fill-neft';
        }

        function formatPercentage(value) {
            var numericValue = Number(value || 0);
            var stdPrecision = getStdPrecision();

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            }) + '%';
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO, precision) {
            var numericValue = Number(value || 0);
            var stdPrecision = normalizePrecision(precision);
            var sign = numericValue < 0 ? '-' : '';
            var absValue = Math.abs(numericValue);

            var amount = absValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });

            if (currencySymbol) {
                return sign + currencySymbol + amount;
            }

            return currencyISO ? sign + amount + ' ' + currencyISO : sign + amount;
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

        function normalizePrecision(precision) {
            var stdPrecision = Number(precision);

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = getStdPrecision();
            }

            return Math.min(stdPrecision, 2);
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

            if ($foot) {
                $foot.toggle(!show);
            }

            if ($body) {
                $body.toggle(!show);
            }
        }

        function setNoData() {
            showState(true, lbl('VAS_033_MessageNoData', 'No Data'));
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;
            $root.remove();
            $card = null;
            $body = null;
            $foot = null;
            $busy = null;
            $state = null;
        };
    };

    VAS.VAS_033_PaymentMethodsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_033_PaymentMethodsWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_033_PaymentMethodsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_033_PaymentMethodsWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
