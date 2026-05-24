/**
 * Payment methods
 * Purpose - Shows the distribution of outgoing AP payments by payment method.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Payment methods                      | VAS_PaymentMethods
 *  2  | WHY                                  | VAS_Why
 *  3  | UPI is cheapest · shift sub-₹2L payments where possible | VAS_PaymentMethodWhy
 *  4  | Loading                              | VAS_Loading
 *  5  | No Data                              | VAS_NoData
 *  6  | Not Specified                        | VAS_NotSpecified
 * ─────────────────────────────────────────────────────────────────────
 */

; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.PaymentMethodsWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-payment-methods-root">');
        var $card;
        var $body;
        var $foot;

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
            var $title = $('<div class="vas-payment-methods-title">').text(lbl('VAS_PaymentMethods', 'Payment methods'));

            $iconBox.append($icon);
            $head.append($iconBox).append($title);

            $body = $('<div class="vas-payment-methods-body">');

            $foot = $('<div class="vas-payment-methods-foot">');
            var $whyTag = $('<span class="vas-payment-methods-why-tag">').text(lbl('VAS_Why', 'WHY'));
            var $whyText = $('<span class="vas-payment-methods-foot-text">').text(lbl('VAS_PaymentMethodWhy', 'UPI is cheapest · shift sub-₹2L payments where possible'));

            $foot.append($whyTag).append($whyText);

            $card.append($head).append($body).append($foot);
            $root.empty().append($card);
        }

        function loadData() {
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'PaymentMethods/GetPaymentMethods',
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
            var methods = $.isArray(data.methods) ? data.methods : [];

            if (methods.length === 0) {
                setNoData();
                return;
            }

            $body.empty();
            $foot.show();

            for (var i = 0; i < methods.length && i < 4; i++) {
                $body.append(createMethodRow(methods[i]));
            }
        }

        function createMethodRow(method) {
            var methodName = method.paymentMethodName || lbl('VAS_NotSpecified', 'Not Specified');
            var percentage = Number(method.percentage || 0);

            if (isNaN(percentage)) {
                percentage = 0;
            }

            if (percentage < 0) {
                percentage = 0;
            }

            if (percentage > 100) {
                percentage = 100;
            }

            var $row = $('<div class="vas-payment-methods-row">');
            var $top = $('<div class="vas-payment-methods-row-top">');
            var $name = $('<span class="vas-payment-methods-name">').text(methodName);
            var $percent = $('<span class="vas-payment-methods-percent">').text(formatPercentage(percentage));
            var $track = $('<div class="vas-payment-methods-track">');
            var $fill = $('<div class="vas-payment-methods-fill">').addClass(getMethodClass(methodName));

            $fill.css('width', percentage + '%');

            $top.append($name).append($percent);
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
            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: 2
            }) + '%';
        }

        function setLoading() {
            if ($foot) {
                $foot.hide();
            }

            if ($body) {
                $body.empty().append($('<div class="vas-payment-methods-state">').text(lbl('VAS_Loading', 'Loading')));
            }
        }

        function setNoData() {
            if ($foot) {
                $foot.hide();
            }

            if ($body) {
                $body.empty().append($('<div class="vas-payment-methods-state">').text(lbl('VAS_NoData', 'No Data')));
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
            $body = null;
            $foot = null;
        };
    };

    VIS.PaymentMethodsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.PaymentMethodsWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.PaymentMethodsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.PaymentMethodsWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);